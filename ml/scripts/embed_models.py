"""
Векторы разных моделей по одним и тем же файлам (tmp/plans/models-v0.7.md).

Вход: инструменталы data/inst/<группа>/*.mp3 (inst_cache.py) и биты линейки data/typebeats/<артист>/*.mp3.
Выход: data/models/<модель>.npz — keys ("<группа>/<id>", биты — "beats/<track_id>") и по массиву на вариант.

  mert    MERT-v1-95M слой 5 — как в приложении (окна 10 с, до 6, нормировка окна)
  effnet  Discogs-EffNet (Essentia): artist / multi / label / release / track (512) и style (1280);
          мел — родной TensorflowInputMusiCNN из essentia, патчи 128 кадров с шагом 62, среднее по патчам
  clap    LAION larger_clap_music, окна 10 с при 48 кГц, среднее
  muq     MuQ-large-msd-iter, окна 10 с при 24 кГц, все слои (выбор слоя — при сравнении)

  nice -n 10 python embed_models.py mert effnet clap muq
"""
import os
import sys
import time
from pathlib import Path

import numpy as np
import torch

BASE = Path(__file__).resolve().parent.parent
OUT = Path(os.environ.get("EMBED_OUT", BASE / "data/models"))
ESSENTIA = Path.home() / "Downloads/essentia-models"
DEVICE = "mps" if torch.backends.mps.is_available() else "cpu"
WINDOW_SECONDS = 10
MAX_WINDOWS = 6


def files():
    for p in sorted((BASE / "data/inst").glob("*/*.mp3")):
        yield f"{p.parent.name}/{p.stem}", p
    for p in sorted((BASE / "data/typebeats").glob("*/*.mp3")):
        yield f"beats/{p.stem}", p


def load(path, rate):
    import librosa
    y, _ = librosa.load(path, sr=rate, mono=True)
    return y.astype(np.float32)


def windows(y, rate):
    """Равномерные окна по WINDOW_SECONDS, как MertEmbedder.SliceWindows."""
    w = WINDOW_SECONDS * rate
    if len(y) <= w:
        return np.pad(y, (0, w - len(y)))[None]
    count = min(MAX_WINDOWS, len(y) // w)
    starts = np.linspace(0, len(y) - w, count).astype(int) if count > 1 else [0]
    return np.stack([y[s:s + w] for s in starts])


class Mert:
    rate = 24000

    def __init__(self):
        from separate_embed import load_mert
        self.model = load_mert()

    @torch.no_grad()
    def __call__(self, y):
        from separate_embed import embed
        return {"layer5": embed(self.model, torch.from_numpy(y))}


class EffNet:
    rate = 16000
    VARIANTS = ("artist", "multi", "label", "release", "track")

    def __init__(self):
        import essentia.standard as es
        import onnxruntime as ort
        self.frontend = es.TensorflowInputMusiCNN()
        self.frame = es.FrameCutter(frameSize=512, hopSize=256, startFromZero=True)
        opts = ort.SessionOptions()
        opts.intra_op_num_threads = 4          # не все ядра: режим ~90%
        self.sessions = {v: ort.InferenceSession(str(ESSENTIA / f"discogs_{v}_embeddings-effnet-bs64-1.onnx"), opts)
                         for v in self.VARIANTS}
        self.style = ort.InferenceSession(str(ESSENTIA / "discogs-effnet-bsdynamic-1.onnx"), opts)

    def patches(self, y):
        import essentia.standard as es
        mel = np.array([self.frontend(f) for f in es.FrameGenerator(y, frameSize=512, hopSize=256, startFromZero=True)])
        if len(mel) < 128:
            mel = np.pad(mel, ((0, 128 - len(mel)), (0, 0)))
        starts = range(0, len(mel) - 128 + 1, 62)
        return np.stack([mel[s:s + 128] for s in starts]).astype(np.float32)

    def __call__(self, y):
        p = self.patches(y)[:64]
        batch = np.zeros((64, 128, 96), np.float32)
        batch[:len(p)] = p
        out = {v: s.run(None, {"melspectrogram": batch})[0][:len(p)].mean(0) for v, s in self.sessions.items()}
        out["style"] = self.style.run(["embeddings"], {"melspectrogram": p})[0].mean(0)
        return out


class Clap:
    rate = 48000

    def __init__(self):
        from transformers import ClapModel, ClapProcessor
        self.processor = ClapProcessor.from_pretrained("laion/larger_clap_music")
        self.model = ClapModel.from_pretrained("laion/larger_clap_music").eval().to(DEVICE)

    @torch.no_grad()
    def __call__(self, y):
        inputs = self.processor(audio=list(windows(y, self.rate)), sampling_rate=self.rate, return_tensors="pt")
        inputs = {k: v.to(DEVICE) for k, v in inputs.items()}
        features = self.model.get_audio_features(**inputs)
        if not torch.is_tensor(features):          # transformers 5.x может вернуть объект с pooler_output
            features = features.pooler_output
        return {"audio": features.mean(0).float().cpu().numpy()}


class MuQModel:
    rate = 24000

    def __init__(self):
        from muq import MuQ
        self.model = MuQ.from_pretrained("OpenMuQ/MuQ-large-msd-iter").eval().to(DEVICE)
        # MuQ держит конфиг конформера в EasyDict, а transformers 5.x ждёт в нём _attn_implementation.
        for module in self.model.modules():
            config = getattr(module, "config", None)
            if config is not None and not hasattr(config, "_attn_implementation"):
                config._attn_implementation = "eager"

        # hidden_states под transformers 5.x не возвращаются (те же грабли, что у MERT) — собираем хуками.
        conformer = self.model.model.conformer
        original = conformer.forward

        def forward(x, **kwargs):
            states = [x]
            hooks = [layer.register_forward_hook(lambda m, i, o: states.append(o[0] if isinstance(o, tuple) else o))
                     for layer in conformer.layers]
            try:
                out = original(x, **kwargs)
            finally:
                for hook in hooks:
                    hook.remove()
            return {"hidden_states": tuple(states), "last_hidden_state": out["last_hidden_state"]}

        conformer.forward = forward

    @torch.no_grad()
    def __call__(self, y):
        batch = torch.from_numpy(windows(y, self.rate)).to(DEVICE)
        # Мимо BaseModelOutput: под transformers 5.x он теряет hidden_states.
        _, hidden = self.model.model.get_predictions(batch, is_features_only=True)
        return {"layers": torch.stack([h.mean(dim=1).mean(dim=0) for h in hidden]).float().cpu().numpy()}


MODELS = {"mert": Mert, "effnet": EffNet, "clap": Clap, "muq": MuQModel}


def run(name, limit=None):
    out = OUT / f"{name}.npz"
    done = dict(np.load(out, allow_pickle=True)) if out.exists() else {}
    have = set(done["keys"].tolist()) if done else set()
    todo = [(k, p) for k, p in files() if k not in have][:limit]
    print(f"{name}: готово {len(have)}, к расчёту {len(todo)}", flush=True)
    if not todo:
        return

    model = MODELS[name]()
    keys = list(done.get("keys", []))
    arrays = {k: list(v) for k, v in done.items() if k != "keys"}
    started = time.time()

    def save():
        OUT.mkdir(parents=True, exist_ok=True)
        np.savez(out.with_suffix(".part.npz"), keys=np.array(keys), **{k: np.stack(v) for k, v in arrays.items()})
        out.with_suffix(".part.npz").rename(out)

    for i, (key, path) in enumerate(todo, 1):
        try:
            vectors = model(load(path, model.rate))
        except Exception as ex:
            print(f"\nпропуск {key}: {ex}")
            continue
        keys.append(key)
        for k, v in vectors.items():
            arrays.setdefault(k, []).append(np.asarray(v, np.float32))
        if i % 200 == 0:
            save()
        print(f"\r{name}: {i}/{len(todo)}  {(time.time() - started) / i:.2f} с/файл   ", end="", flush=True)
        time.sleep(0.05)       # короткая пауза: не душить машину целиком

    save()
    print(f"\n{name}: сохранено {len(keys)} -> {out}")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--limit")]
    limit = next((int(a.split("=")[1]) for a in sys.argv[1:] if a.startswith("--limit=")), None)
    for name in args or MODELS:
        run(name, limit)

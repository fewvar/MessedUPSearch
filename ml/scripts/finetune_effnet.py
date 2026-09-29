"""
Шаг 2: дообучение самой EffNet (tmp/plans/finetune-v0.7.md).

  mel     — мел всех файлов один раз -> data/mel/<группа>/<id>.npy (float16), тем же фронтендом,
            что в приложении (окно и мел-матрица из effnet_head.bin; сверка с Essentia в начале)
  train   — EffNet из ONNX (onnx2torch), размораживаются последние слои (--unfreeze top|block1|block2),
            + голова W; supervised contrastive «треки одного андеграунд-артиста рядом»;
            ранняя остановка по 20% отложенных андеграунд-артистов (LOO top-5); ориентиры и биты — только проверка
  eval    — векторы всех файлов дообученной сетью -> та же проверка (682, Deezer+SC, хабы по группам),
            парный бутстреп против текущей модели (EffNet style + голова) на тех же данных

  nice -n 10 python finetune_effnet.py mel
  nice -n 10 python finetune_effnet.py train --unfreeze block1
"""
import argparse
import struct
import time
from pathlib import Path

import numpy as np
import torch

import compare_models as cm

BASE = cm.BASE
MODELS = BASE.parent / "MessedUpSearchA/Assets/Models"
MEL = BASE / "data/mel"
OUT = BASE / "data/finetune"
DEVICE = "mps" if torch.backends.mps.is_available() else "cpu"
PATCH, PATCH_HOP, HOP = 128, 62, 256
TAU = 0.1
PAUSE = 0.15          # пауза между шагами: машина не занята на 100% (режим ~90%)

UNFREEZE = {
    "top": ["Conv_234"],
    "block1": ["Conv_220", "Conv_223", "Conv_227", "Conv_230", "Conv_233", "Conv_234"],
    "block2": ["Conv_205", "Conv_208", "Conv_212", "Conv_215", "Conv_218",
               "Conv_220", "Conv_223", "Conv_227", "Conv_230", "Conv_233", "Conv_234"],
}


def read_head():
    with open(MODELS / "effnet_head.bin", "rb") as f:
        assert f.read(4) == b"MUSH"
        _, frame, bands, dim = struct.unpack("<iiii", f.read(16))
        window = np.frombuffer(f.read(4 * frame), "<f4")
        mel = np.frombuffer(f.read(4 * bands * (frame // 2 + 1)), "<f4").reshape(bands, frame // 2 + 1)
        c0 = np.frombuffer(f.read(4 * dim), "<f4")
        W = np.frombuffer(f.read(2 * dim * dim), "<f2").astype(np.float32).reshape(dim, dim)
        c2 = np.frombuffer(f.read(4 * dim), "<f4")
    return window, mel, c0, W, c2


def mel_frames(y, window, mel):
    """Как EffNetEmbedder.MelFrames: кадры 512/256, пока от начала кадра до конца > 256, хвост нулями."""
    starts = np.arange(0, max(1, len(y) - HOP), HOP)
    starts = starts[len(y) - starts > HOP] if len(y) > HOP else starts[:1]
    padded = np.pad(y, (0, 512))
    idx = starts[:, None] + np.arange(512)[None, :]
    spec = np.abs(np.fft.rfft(padded[idx] * window, axis=1)) ** 2
    out = np.log10(1 + 10000 * spec @ mel.T).astype(np.float32)
    if len(out) < PATCH:
        out = np.pad(out, ((0, PATCH - len(out)), (0, 0)))
    return out


def all_files():
    for p in sorted((BASE / "data/inst").glob("*/*.mp3")):
        yield f"{p.parent.name}/{p.stem}", p
    for p in sorted((BASE / "data/typebeats").glob("*/*.mp3")):
        yield f"beats/{p.stem}", p


def build_mel():
    from embed_models import EffNet, load
    import essentia.standard as es

    window, mel, *_ = read_head()
    # Сверка с Essentia на одном файле: векторный numpy-фронтенд = TensorflowInputMusiCNN.
    key, path = next(all_files())
    y = load(path, 16000)
    ref = np.array([es.TensorflowInputMusiCNN()(f) for f in es.FrameGenerator(y, frameSize=512, hopSize=256, startFromZero=True)])
    mine = mel_frames(y, window, mel)[:len(ref)]
    assert np.abs(ref - mine).max() < 1e-3, f"мел не совпал: {np.abs(ref - mine).max()}"
    print(f"мел = Essentia (макс. отклонение {np.abs(ref - mine).max():.1e})")

    files = list(all_files())
    started = time.time()
    for i, (key, path) in enumerate(files, 1):
        out = MEL / f"{key}.npy"
        if out.exists():
            continue
        out.parent.mkdir(parents=True, exist_ok=True)
        y = load(path, 16000)
        cap = 60 * 16000
        if len(y) > cap:
            y = y[(len(y) - cap) // 2:][:cap]
        np.save(out, mel_frames(y, window, mel).astype(np.float16))
        if i % 200 == 0:
            print(f"\r{i}/{len(files)}  {(time.time() - started) / i:.2f} с/файл", end="", flush=True)
    print(f"\nмел готов: {len(files)} файлов")


def load_net():
    import onnx
    from onnx2torch import convert
    return convert(onnx.load(str(MODELS / "effnet_style.onnx")))


def embed(net, patches):
    out = net(patches)
    return out[1] if isinstance(out, (list, tuple)) else out


def patches_of(frames, count=None, rng=None):
    starts = np.arange(0, len(frames) - PATCH + 1, PATCH_HOP)
    if count is not None and rng is not None:
        starts = rng.choice(starts, size=min(count, len(starts)), replace=False)
    return np.stack([frames[s:s + PATCH] for s in starts])


@torch.no_grad()
def embed_files(net, keys, max_patches=None, batch=256):
    """Файл -> среднее embeddings по патчам (max_patches — равномерно, для быстрой проверки)."""
    net.eval()
    vectors = []
    for key in keys:
        frames = np.load(MEL / f"{key}.npy").astype(np.float32)
        p = patches_of(frames)
        if max_patches and len(p) > max_patches:
            p = p[np.linspace(0, len(p) - 1, max_patches).astype(int)]
        chunks = [embed(net, torch.from_numpy(p[i:i + batch]).to(DEVICE)) for i in range(0, len(p), batch)]
        vectors.append(torch.cat(chunks).mean(0).cpu().numpy())
    return np.stack(vectors)


def loo_top5(Z, a):
    import head_underground as hu
    return hu.loo_top5(cm.unit(Z), a)


def dataset():
    label = cm.labels()
    keys = [k for k, _ in all_files()]
    lab = [label(k) for k in keys]
    keys = np.array(keys)
    roles = np.array([r or "" for r, _ in lab])
    artists = np.array([a or "" for _, a in lab])
    ok = artists != ""
    return keys[ok], roles[ok], artists[ok]


def train(unfreeze, epochs=150, patience=12, seed=0):
    rng = np.random.default_rng(seed)
    torch.manual_seed(seed)
    keys, roles, artists = dataset()
    _, _, c0, W0, _ = read_head()

    und = roles == "und"
    counts = dict(zip(*np.unique(artists[und], return_counts=True)))
    und &= np.array([counts.get(x, 0) >= cm.MIN_TRACKS for x in artists])
    names = np.unique(artists[und])
    held = set(rng.choice(names, size=len(names) // 5, replace=False))
    val = und & np.array([x in held for x in artists])
    tr = und & ~val
    tr_keys, tr_art = keys[tr], artists[tr]
    by_artist = {}
    for k, a in zip(tr_keys, tr_art):
        by_artist.setdefault(a, []).append(k)
    by_artist = {a: v for a, v in by_artist.items() if len(v) >= 2}
    print(f"обучение: {len(by_artist)} артистов, {sum(map(len, by_artist.values()))} треков; "
          f"проверка: {len(held)} отложенных артистов, {val.sum()} треков")

    mel_cache = {k: np.load(MEL / f"{k}.npy") for k in list(tr_keys) + list(keys[val])}

    net = load_net().to(DEVICE)
    trainable = [p for n, p in net.named_parameters() if n.split(".")[0] in UNFREEZE[unfreeze]]
    for p in net.parameters():
        p.requires_grad_(False)
    for p in trainable:
        p.requires_grad_(True)
    originals = [p.detach().clone() for p in trainable]
    c0_t = torch.tensor(c0, device=DEVICE)
    W = torch.nn.Parameter(torch.tensor(W0, device=DEVICE))
    opt = torch.optim.Adam([{"params": trainable, "lr": 1e-5}, {"params": [W], "lr": 1e-4}])
    print(f"размораживаю {unfreeze}: {sum(p.numel() for p in trainable):,} параметров сети + голова")

    def project(e):
        return torch.nn.functional.normalize(torch.nn.functional.normalize(e - c0_t, dim=1) @ W, dim=1)

    def validate():
        E = embed_files(net, keys[val], max_patches=8)
        with torch.no_grad():
            Z = project(torch.tensor(E, device=DEVICE)).cpu().numpy()
        return loo_top5(Z, artists[val])

    best = validate()
    print(f"старт: отложенные LOO top-5 {best:.1%}")
    best_state = ({n: p.detach().cpu().clone() for n, p in net.named_parameters() if p.requires_grad}, W.detach().cpu().clone())
    stale = 0
    artists_list = list(by_artist)
    per_batch = 32

    for epoch in range(epochs):
        net.train()
        rng.shuffle(artists_list)
        losses = []
        started = time.time()
        for b in range(0, len(artists_list) - per_batch + 1, per_batch):
            group = artists_list[b:b + per_batch]
            batch, labels = [], []
            for j, a in enumerate(group):
                for k in rng.choice(by_artist[a], size=2, replace=False):
                    frames = mel_cache[k].astype(np.float32)
                    batch.append(patches_of(frames, 2, rng))       # 2 случайных патча трека
                    labels.append(j)
            x = torch.from_numpy(np.concatenate(batch)).to(DEVICE)
            gain = torch.empty(len(x), 1, 1, device=DEVICE).uniform_(-0.15, 0.15)   # громкость в лог-меле
            e = embed(net, x + gain).view(len(labels), 2, -1).mean(1)
            z = project(e)
            y = torch.tensor(labels, device=DEVICE)
            same = (y[:, None] == y[None, :]).float()
            same.fill_diagonal_(0)
            logits = z @ z.T / TAU
            logits = logits - torch.eye(len(z), device=DEVICE) * 1e9
            log_prob = logits - torch.logsumexp(logits, dim=1, keepdim=True)
            loss = -((log_prob * same).sum(1) / same.sum(1).clamp(min=1)).mean()
            # Не уходить далеко от исходных весов: 650 артистов — мало, сеть легко переобучить.
            loss = loss + 1e-2 * sum(((p - o) ** 2).sum() for p, o in zip(trainable, originals)) \
                + ((W - torch.tensor(W0, device=DEVICE)) ** 2).sum()
            opt.zero_grad()
            loss.backward()
            opt.step()
            losses.append(loss.item())
            time.sleep(PAUSE)

        score = validate()
        print(f"эпоха {epoch + 1}: потеря {np.mean(losses):.3f}, отложенные LOO top-5 {score:.1%}, "
              f"{time.time() - started:.0f} с", flush=True)
        if score > best + 1e-4:
            best, stale = score, 0
            best_state = ({n: p.detach().cpu().clone() for n, p in net.named_parameters() if p.requires_grad}, W.detach().cpu().clone())
        else:
            stale += 1
            if stale >= patience:
                break

    OUT.mkdir(parents=True, exist_ok=True)
    torch.save({"unfreeze": unfreeze, "params": best_state[0], "W": best_state[1], "best": float(best)},
               OUT / f"{unfreeze}.pt")
    print(f"лучшее: отложенные LOO top-5 {best:.1%} -> {OUT / f'{unfreeze}.pt'}")


def evaluate(unfreeze):
    """Все файлы дообученной сетью -> проверка на 682 против текущей модели (те же данные)."""
    keys, roles, artists = dataset()
    _, _, c0, W0, _ = read_head()
    merged = np.where(np.isin(roles, ["deezer", "sc"]), "ref", roles)

    base_net = load_net().to(DEVICE)
    state = torch.load(OUT / f"{unfreeze}.pt", weights_only=False)   # свой файл, из этого же скрипта
    tuned = load_net().to(DEVICE)
    with torch.no_grad():
        for n, p in tuned.named_parameters():
            if n in state["params"]:
                p.copy_(state["params"][n].to(DEVICE))

    results = {}
    for name, net, W in (("текущая", base_net, W0), (f"дообучена ({unfreeze})", tuned, state["W"].numpy())):
        E = embed_files(net, keys)
        Z = cm.unit(cm.unit(E - c0) @ W)
        ids, r30, ra, _, n_all = cm.evaluate(Z, merged, artists, "ref")
        results[name] = (r30, ra)
        print(f"{name:<24} top-5/30 {(r30 < 5).mean():.1%}  top-5/все {(ra < 5).mean():.1%}  "
              f"top-10/все {(ra < 10).mean():.1%}  медиана {int(np.median(ra)) + 1} из {n_all}", flush=True)

    (b30, ball), (t30, tall) = results.values()
    lo, hi = cm.bootstrap((tall < 10).astype(float), (ball < 10).astype(float))
    lo5, hi5 = cm.bootstrap((t30 < 5).astype(float), (b30 < 5).astype(float))
    print(f"прирост: top-10/все {((tall < 10).mean() - (ball < 10).mean()) * 100:+.1f} п.п. [{lo:+.1f}; {hi:+.1f}]  "
          f"top-5/30 {((t30 < 5).mean() - (b30 < 5).mean()) * 100:+.1f} п.п. [{lo5:+.1f}; {hi5:+.1f}]")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["mel", "train", "eval"])
    parser.add_argument("--unfreeze", default="block1", choices=list(UNFREEZE))
    args = parser.parse_args()
    torch.set_num_threads(max(1, (torch.get_num_threads() * 3) // 4))
    {"mel": build_mel, "train": lambda: train(args.unfreeze), "eval": lambda: evaluate(args.unfreeze)}[args.command]()

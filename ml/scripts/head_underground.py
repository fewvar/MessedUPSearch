"""
Голова поверх EffNet style, обученная на андеграунде (29.09.2026).

Прошлая голова (train_head.py) училась на 30 ориентирах и шумных type beat'ах — в шуме.
Здесь метки чистые: «треки одного андеграунд-артиста рядом» (supervised contrastive, весь батч сразу).
Ориентиры и биты в обучение не попадают — проверка на них честная, как в продукте: индекс знаком,
бит новый.

  обучение:        80% андеграунд-артистов
  ранняя остановка: 20% отложенных — leave-one-track-out top-5 среди них
  итог:            compare_models.evaluate на битах (682 артиста), парный бутстреп против «без головы»

  python head_underground.py [--features style|style+muq8] [--dims 128,256,full]
"""
import argparse

import numpy as np
import torch

import compare_models as cm

BASE = cm.BASE
TAU = 0.1


def load(features):
    label = cm.labels()
    models = ("effnet", "muq") if "muq" in features else ("effnet",)
    z = {m: np.load(BASE / f"data/models/{m}.npz", allow_pickle=True) for m in models}
    keys = z["effnet"]["keys"].astype(str)
    if "muq" in z:
        assert (z["muq"]["keys"].astype(str) == keys).all()
    lab = [label(k) for k in keys]
    roles = np.array([r or "" for r, _ in lab])
    artists = np.array([a or "" for _, a in lab])
    ok = artists != ""

    parts = [z["effnet"]["style"]]
    if features == "style+muq8":
        parts.append(z["muq"]["layers"][:, 8])
    # Каждая часть: центр + единичная длина, затем склейка — чтобы ни одна модель не доминировала по масштабу.
    X = np.hstack([cm.unit(p[ok].astype(np.float32) - p[ok].mean(0)) for p in parts])
    return X, roles[ok], artists[ok]


def loo_top5(Z, a):
    """Leave-one-track-out: трек против остальных треков, артист по top-3 — как в приложении."""
    sims = Z @ Z.T
    np.fill_diagonal(sims, -np.inf)
    names = np.unique(a)
    idx = {n: i for i, n in enumerate(names)}
    ai = np.array([idx[x] for x in a])
    scores = np.full((len(Z), len(names)), -np.inf, np.float32)
    for j in range(len(names)):
        part = np.sort(sims[:, ai == j], axis=1)[:, -cm.TOP_K:]
        part = np.where(np.isfinite(part), part, np.nan)
        scores[:, j] = np.nanmean(part, axis=1)
    scores = np.nan_to_num(scores, nan=-np.inf)
    truth = scores[np.arange(len(Z)), ai]
    return ((scores > truth[:, None]).sum(1) < 5).mean()


def train(X, a, dim, seed=0, epochs=600, patience=60):
    rng = np.random.default_rng(seed)
    torch.manual_seed(seed)
    names = np.unique(a)
    held = set(rng.choice(names, size=len(names) // 5, replace=False))
    val = np.array([x in held for x in a])

    idx = {n: i for i, n in enumerate(names)}
    y = torch.tensor([idx[x] for x in a[~val]])
    Xt = torch.tensor(X[~val])
    same = (y[:, None] == y[None, :]).float()
    same.fill_diagonal_(0)
    has_pos = same.sum(1) > 0

    full = dim == X.shape[1]
    start = torch.eye(dim) if full else torch.nn.init.orthogonal_(torch.empty(X.shape[1], dim))
    W = torch.nn.Parameter(start.clone())
    opt = torch.optim.Adam([W], lr=1e-3, weight_decay=0 if full else 1e-4)

    best, best_W, stale = loo_top5(cm.unit(X[val] @ W.detach().numpy()), a[val]), W.detach().clone(), 0
    start_score = best
    for epoch in range(epochs):
        Z = torch.nn.functional.normalize(Xt @ W, dim=1)
        logits = Z @ Z.T / TAU
        logits.fill_diagonal_(-1e9)
        log_prob = logits - torch.logsumexp(logits, dim=1, keepdim=True)
        loss = -((log_prob * same).sum(1) / same.sum(1).clamp(min=1))[has_pos].mean()
        if full:
            loss = loss + ((W - start) ** 2).sum()
        opt.zero_grad()
        loss.backward()
        opt.step()

        if epoch % 5 == 4:
            score = loo_top5(cm.unit(X[val] @ W.detach().numpy()), a[val])
            if score > best + 1e-4:
                best, best_W, stale = score, W.detach().clone(), 0
            else:
                stale += 5
                if stale >= patience:
                    break
    return best_W.numpy(), start_score, best, epoch + 1


def report(name, X, roles, artists, base):
    out = {}
    for ref in ("deezer", "sc"):
        ids, r30, ra, _, _ = cm.evaluate(X, roles, artists, ref)
        out[ref] = (ids, r30, ra)
        line = f"{name:<22}{ref:<7} top-5/30 {(r30 < 5).mean():6.1%}  top-5/все {(ra < 5).mean():5.1%}  " \
               f"top-10/все {(ra < 10).mean():5.1%}  медиана {int(np.median(ra)) + 1:4}"
        if base is not None:
            b_ids, b30, bra = base[ref]
            assert (b_ids == ids).all()
            lo, hi = cm.bootstrap((ra < 10).astype(float), (bra < 10).astype(float))
            lo5, hi5 = cm.bootstrap((r30 < 5).astype(float), (b30 < 5).astype(float))
            line += f"   Δtop-10/все {((ra < 10).mean() - (bra < 10).mean()) * 100:+.1f} [{lo:+.1f}; {hi:+.1f}]" \
                    f"  Δtop-5/30 {((r30 < 5).mean() - (b30 < 5).mean()) * 100:+.1f} [{lo5:+.1f}; {hi5:+.1f}]"
        print(line)
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--features", default="style")
    parser.add_argument("--dims", default="128,256,full")
    parser.add_argument("--save", default="", help="размерность, веса которой сохранить в data/head_<features>.npy")
    args = parser.parse_args()

    X, roles, artists = load(args.features)
    und = roles == "und"
    counts = dict(zip(*np.unique(artists[und], return_counts=True)))
    train_mask = und & np.array([counts.get(x, 0) >= cm.MIN_TRACKS for x in artists])
    print(f"признаки {args.features} ({X.shape[1]}), андеграунд для обучения: {train_mask.sum()} треков, "
          f"{len(set(artists[train_mask]))} артистов\n")

    base = report("без головы", X, roles, artists, None)
    for d in args.dims.split(","):
        dim = X.shape[1] if d == "full" else int(d)
        W, before, after, epochs = train(X[train_mask], artists[train_mask], dim)
        print(f"  d={d}: отложенные андеграунд-артисты LOO top-5 {before:.1%} -> {after:.1%}, эпох {epochs}")
        report(f"голова d={d}", cm.unit(X @ W), roles, artists, base)
        if args.save == d:
            np.save(BASE / f"data/head_{args.features.replace('+', '_')}.npy", W.astype(np.float32))


if __name__ == "__main__":
    main()

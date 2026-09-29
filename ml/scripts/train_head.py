"""
Шаг 3 плана качества: обучаемая линейная голова поверх MERT (tmp/plans/head-v0.7.md).

Проекция W (768 -> d), общая для бита и для инструменталов артистов, учится так, чтобы
«X type beat» был ближе к трекам X. Проверка — на АРТИСТАХ, которых голова не видела:
30 артистов -> 5 групп по 6; учим на 24, проверяем биты 6 отложенных среди всех 30.
Так же, как работает продукт: 652 андеграунд-артиста индекса голова тоже не видела.

Фон для поправки на хабы в каждой группе — только биты обучающих артистов (иначе утечка).
Сравнение с нынешним вариантом (без головы) — на тех же битах, парным бутстрепом.

  python train_head.py              # проверка по группам, отчёт
  python train_head.py --final 128  # обучить на всех 30 и сохранить data/head_w.npy
"""
import argparse
import struct
from pathlib import Path

import numpy as np
import torch

BASE = Path(__file__).resolve().parent.parent
LAYER = 5
TOP_K = 3
FOLDS = 5
TAU = 0.07


def unit(m):
    return m / (np.linalg.norm(m, axis=-1, keepdims=True) + 1e-9)


def load_center():
    with open(BASE / "crawler/Mert/references_v2.bin", "rb") as f:
        assert f.read(4) == b"MUSX"
        _, dim, _ = struct.unpack("<iii", f.read(12))
        return np.frombuffer(f.read(4 * dim), "<f4").copy()


def load():
    center = load_center()
    q = np.load(BASE / "data/embeddings_typebeats.npz", allow_pickle=True)
    g = np.load(BASE / "data/embeddings_vocal_inst.npz", allow_pickle=True)

    names = sorted(set(g["artists"].astype(str)) & set(q["artists"].astype(str)))
    index = {n: i for i, n in enumerate(names)}

    g_keep = np.isin(g["artists"].astype(str), names)
    q_keep = np.isin(q["artists"].astype(str), names)

    G = unit(g["embeddings"][g_keep, LAYER].astype(np.float32) - center)
    Q = unit(q["embeddings"][q_keep, LAYER].astype(np.float32) - center)
    ga = np.array([index[a] for a in g["artists"][g_keep].astype(str)])
    qa = np.array([index[a] for a in q["artists"][q_keep].astype(str)])
    return names, G, ga, Q, qa


def artist_scores(Q, G, ga, n):
    """(биты, артисты): среднее по TOP_K ближайшим трекам артиста — как в приложении."""
    sims = Q @ G.T
    out = np.full((len(Q), n), -np.inf, dtype=np.float32)
    for a in range(n):
        part = sims[:, ga == a]
        k = min(TOP_K, part.shape[1])
        out[:, a] = np.sort(part, axis=1)[:, -k:].mean(axis=1)
    return out


def ranks_with_hubs(Q, G, ga, n, background, truth):
    scores = artist_scores(Q, G, ga, n) - artist_scores(background, G, ga, n).mean(axis=0, keepdims=True)
    order = np.argsort(-scores, axis=1)
    return np.array([np.where(order[i] == truth[i])[0][0] for i in range(len(truth))])


def project(W, X):
    return unit(X @ W)


# Штраф за уход от тождественного преобразования (только для d = 768): голова стартует с «как сейчас»
# и может только подправлять — на малых данных случайная проекция 768 -> 128 переобучалась.
IDENTITY_PENALTY = 1.0


def train(Qt, qt, Gt, gt, n_train, dim, seed, epochs=400, patience=40):
    """
    InfoNCE: бит против прототипов обучающих артистов. Ранняя остановка по отложенной части битов.
    d = 768 — старт с единичной матрицы и штраф ||W - I||²: «поправка к тому, что есть».
    d < 768 — случайная ортогональная проекция (сжатие).
    """
    rng = np.random.default_rng(seed)
    torch.manual_seed(seed)

    val = rng.random(len(qt)) < 0.2
    Qtr, qtr, Qva, qva = map(torch.tensor, (Qt[~val], qt[~val], Qt[val], qt[val]))
    Gt_, gt_ = torch.tensor(Gt), torch.tensor(gt)

    identity = dim == Qt.shape[1]
    start = torch.eye(dim) if identity else torch.nn.init.orthogonal_(torch.empty(Qt.shape[1], dim))
    W = torch.nn.Parameter(start.clone())
    opt = torch.optim.Adam([W], lr=1e-3, weight_decay=0 if identity else 1e-4)

    def prototypes():
        P = torch.nn.functional.normalize(Gt_ @ W, dim=1)
        protos = torch.stack([P[gt_ == a].mean(0) for a in range(n_train)])
        return torch.nn.functional.normalize(protos, dim=1)

    best, best_W, stale = -1.0, W.detach().clone(), 0
    for epoch in range(epochs):
        logits = torch.nn.functional.normalize(Qtr @ W, dim=1) @ prototypes().T / TAU
        loss = torch.nn.functional.cross_entropy(logits, qtr)
        if identity:
            loss = loss + IDENTITY_PENALTY * ((W - start) ** 2).sum()
        opt.zero_grad()
        loss.backward()
        opt.step()

        with torch.no_grad():
            val_logits = torch.nn.functional.normalize(Qva @ W, dim=1) @ prototypes().T
            acc = (val_logits.argsort(dim=1, descending=True)[:, :5] == qva[:, None]).any(1).float().mean().item()
        if acc > best + 1e-4:
            best, best_W, stale = acc, W.detach().clone(), 0
        else:
            stale += 1
            if stale >= patience:
                break

    return best_W.numpy(), epoch + 1


def cross_validate(dims):
    names, G, ga, Q, qa = load()
    n = len(names)
    print(f"артистов {n}, битов {len(Q)}, треков-инструменталов {len(G)}; случайно top-5 = {5 / n:.1%}\n")

    order = np.random.default_rng(0).permutation(n)
    folds = np.array_split(order, FOLDS)

    base_ranks, head_ranks = [], {d: [] for d in dims}
    epochs = {d: [] for d in dims}

    for f, held in enumerate(folds):
        test = np.isin(qa, held)
        train_artists = np.setdiff1d(np.arange(n), held)
        remap = {a: i for i, a in enumerate(train_artists)}

        background = Q[~test]                       # только биты обучающих артистов
        truth = qa[test]

        base_ranks.append(ranks_with_hubs(Q[test], G, ga, n, background, truth))

        tq = ~test
        tg = np.isin(ga, train_artists)
        for d in dims:
            W, ep = train(Q[tq], np.array([remap[a] for a in qa[tq]]), G[tg],
                          np.array([remap[a] for a in ga[tg]]), len(train_artists), d, seed=f)
            epochs[d].append(ep)
            head_ranks[d].append(ranks_with_hubs(project(W, Q[test]), project(W, G), ga, n,
                                                 project(W, background), truth))
        print(f"группа {f + 1}: отложено {len(held)} артистов, {test.sum()} битов")

    base = np.concatenate(base_ranks)
    print(f"\nбез головы (как сейчас):  top-1 {(base == 0).mean():.1%}  top-5 {(base < 5).mean():.1%}")

    rng = np.random.default_rng(0)
    for d in dims:
        head = np.concatenate(head_ranks[d])
        diffs = []
        for _ in range(2000):
            k = rng.integers(0, len(base), len(base))
            diffs.append((head[k] < 5).mean() - (base[k] < 5).mean())
        lo, hi = np.percentile(diffs, [5, 95])
        print(f"голова d={d:<4} top-1 {(head == 0).mean():.1%}  top-5 {(head < 5).mean():.1%}  "
              f"разница {np.mean(diffs) * 100:+.1f} п.п. [90% ДИ {lo * 100:+.1f}; {hi * 100:+.1f}]  "
              f"эпох ~{int(np.mean(epochs[d]))}")


def final(dim):
    names, G, ga, Q, qa = load()
    n = len(names)
    W, ep = train(Q, qa, G, ga, n, dim, seed=0)
    out = BASE / "data/head_w.npy"
    np.save(out, W.astype(np.float32))
    print(f"{out}: {W.shape}, эпох {ep}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--dims", default="64,128,256,768")
    parser.add_argument("--final", type=int, default=0)
    args = parser.parse_args()

    if args.final:
        final(args.final)
    else:
        cross_validate([int(d) for d in args.dims.split(",")])

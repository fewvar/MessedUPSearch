"""
Шаг 1 (29.09.2026): докачка треков андеграунду (3.8 -> ~8 кусков на артиста) — даёт ли прирост.

Было: андеграунд — первые 4989 записей effnet.npz (до докачки). Стало: все записи.
Для каждого — голова заново (head_underground.train), та же проверка: ориентиры Deezer+SC,
877 type beat'ов, 682 артиста, хабы по группам. Парный бутстреп «стало против было».

  python eval_more_tracks.py
"""
import numpy as np

import compare_models as cm
import head_underground as hu

OLD_COUNT = 4989     # записей в effnet.npz до докачки (ночь 28–29.09)


def run(X, roles, artists, keep):
    Xk, rk, ak = X[keep], roles[keep], artists[keep]
    und = rk == "und"
    counts = dict(zip(*np.unique(ak[und], return_counts=True)))
    train = und & np.array([counts.get(x, 0) >= cm.MIN_TRACKS for x in ak])
    W, before, after, epochs = hu.train(Xk[train], ak[train], X.shape[1])
    merged = np.where(np.isin(rk, ["deezer", "sc"]), "ref", rk)
    ids, r30, ra, _, n_all = cm.evaluate(cm.unit(Xk @ W), merged, ak, "ref")
    print(f"  андеграунд: {und.sum()} треков, в среднем {und.sum() / len(set(ak[und])):.1f} на артиста; "
          f"голова: отложенные {before:.1%} -> {after:.1%}, эпох {epochs}")
    print(f"  top-5/30 {(r30 < 5).mean():.1%}  top-5/все {(ra < 5).mean():.1%}  top-10/все {(ra < 10).mean():.1%}  "
          f"медиана {int(np.median(ra)) + 1} из {n_all}")
    return r30, ra


def main():
    X, roles, artists = hu.load("style")
    n = len(np.load(cm.BASE / "data/models/effnet.npz", allow_pickle=True)["keys"])
    label = cm.labels()
    keys = np.load(cm.BASE / "data/models/effnet.npz", allow_pickle=True)["keys"].astype(str)
    ok = np.array([label(k)[1] is not None and label(k)[1] != "" for k in keys])
    position = np.arange(n)[ok]                       # номер записи в npz для каждой строки X

    old = (roles != "und") | (position < OLD_COUNT)
    print("было (до докачки):")
    o30, oall = run(X, roles, artists, old)
    print("стало (все треки):")
    n30, nall = run(X, roles, artists, np.ones(len(X), bool))

    lo, hi = cm.bootstrap((nall < 10).astype(float), (oall < 10).astype(float))
    lo5, hi5 = cm.bootstrap((n30 < 5).astype(float), (o30 < 5).astype(float))
    print(f"\nприрост: top-10/все {((nall < 10).mean() - (oall < 10).mean()) * 100:+.1f} п.п. [{lo:+.1f}; {hi:+.1f}]  "
          f"top-5/30 {((n30 < 5).mean() - (o30 < 5).mean()) * 100:+.1f} п.п. [{lo5:+.1f}; {hi5:+.1f}]")


if __name__ == "__main__":
    main()

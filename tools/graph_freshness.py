#!/usr/bin/env python3
"""Проверка свежести графа знаний относительно рабочего дерева.

Зачем: граф в graphify-out/ — это кэш структуры проекта. Пересборка привязана
к коммиту (post-commit hook), а цикл работы агента «правка -> чтение графа ->
правка» границу коммита не пересекает. Без проверки на стороне чтения агент
молча отвечает по устаревшему кэшу — так, например, созданный после последней
сборки модуль для агента просто не существует.

Одна проверка, три точки вызова:

    python tools/graph_freshness.py                    # человек/агент вручную
    python tools/graph_freshness.py --hook SessionStart  # хук Claude Code
    python tools/graph_freshness.py --hook PostToolUse   # хук на Write
    python tools/graph_freshness.py --ci                 # гейт в CI

Признак устаревания подбирается под точку вызова. Локально сравниваются mtime
файлов с временем, записанным в manifest.json. В CI mtime бесполезны — checkout
проставляет всем файлам время выгрузки, — поэтому сравнивается время последнего
коммита, тронувшего корпус, с временем последнего коммита, тронувшего граф.

Коды возврата (кроме режима --hook, который всегда завершается нулём, чтобы
не блокировать работу):
    0 — граф покрывает корпус,
    1 — расхождение,
    2 — граф отсутствует или манифест нечитаем.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

# Базовый набор типов корпуса. Это нижняя граница, а не истина в последней
# инстанции: фактический набор берётся объединением с типами, которые graphify
# уже записал в манифест (см. tracked_suffixes).
#
# Почему не просто список: жёстко заданный здесь список — вторая рукописная
# копия того же факта, что и в CLAUDE.md. Две копии расходятся молча; так и
# случилось с `.py`, который graphify индексирует, а список не упоминал, из-за
# чего правки самого проверщика не считались ломающими граф.
BASELINE_SUFFIXES = {
    ".cs", ".xaml", ".csproj", ".slnx", ".json", ".md", ".yml", ".yaml", ".png",
}

# Заполняется из манифеста при старте: см. tracked_suffixes().
TRACKED_SUFFIXES: set[str] = set(BASELINE_SUFFIXES)

# Артефакты самого графа исключены: они меняются каждой пересборкой и иначе
# вызвали бы вечное «граф устарел относительно самого себя».
EXCLUDED_PREFIXES = ("graphify-out/",)

# Допуск на сравнение mtime: git и разные ФС округляют время записи по-разному.
MTIME_TOLERANCE_SEC = 2.0

UPDATE_HINT = "обновить: /graphify . --update"


def force_utf8_streams() -> None:
    """Потоки в UTF-8 независимо от кодовой страницы Windows.

    Без этого Python на русской Windows пишет stdout в cp1251, а Claude Code и
    GitHub Actions читают UTF-8: предупреждение о протухшем графе само приезжает
    нечитаемым. Тот же класс отказа, что BOM в служебных путях graphify.
    """
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError, ValueError):
            pass


def git(root: Path | None, *args: str, timeout: int = 120) -> str | None:
    """Вызов git. None означает, что вызов не удался — это не то же самое, что пустой вывод."""
    try:
        done = subprocess.run(
            ["git", *args],
            cwd=str(root) if root else None,
            capture_output=True, text=True, timeout=timeout,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    return done.stdout if done.returncode == 0 else None


def repo_root() -> Path:
    """Корень репозитория. Хуки запускаются с разным cwd, полагаться на него нельзя."""
    out = git(None, "rev-parse", "--show-toplevel", timeout=30)
    if out and out.strip():
        return Path(out.strip())
    return Path(__file__).resolve().parent.parent


def is_corpus_path(path: str) -> bool:
    return (
        not path.startswith(EXCLUDED_PREFIXES)
        and Path(path).suffix.lower() in TRACKED_SUFFIXES
    )


def corpus_on_disk(root: Path) -> set[str] | None:
    """Файлы, которые увидел бы graphify: под контролем git либо новые и не игнорируемые."""
    out = git(root, "ls-files", "-c", "-o", "--exclude-standard", "-z")
    if out is None:
        return None
    files = set()
    for raw in out.split("\0"):
        path = raw.strip().replace("\\", "/")
        if not path or not is_corpus_path(path):
            continue
        # git отдаёт индекс, а не файловую систему: удалённый, но ещё не
        # закоммиченный файл остаётся в выдаче и дал бы ложную «правку».
        if not (root / path).is_file():
            continue
        files.add(path)
    return files


def load_manifest(root: Path) -> dict[str, float] | int:
    """Пути из манифеста со временем, на которое граф их видел. int — код ошибки."""
    path = root / "graphify-out" / "manifest.json"
    if not path.is_file():
        return 2
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return 2
    if not isinstance(raw, dict):
        return 2

    # Набор типов расширяется тем, что graphify фактически положил в манифест.
    # Так проверщик следует за инструментом, а не за копией его правил.
    # Обязательно до фильтрации: иначе типы вне базового набора отсеются здесь же.
    TRACKED_SUFFIXES.update(
        Path(key.replace("\\", "/")).suffix.lower() for key in raw
    )

    known = {}
    for key, meta in raw.items():
        norm = key.replace("\\", "/")
        if not is_corpus_path(norm):
            continue
        mtime = meta.get("mtime") if isinstance(meta, dict) else None
        known[norm] = float(mtime) if isinstance(mtime, (int, float)) else 0.0
    return known


def render(paths: list[str], limit: int) -> str:
    shown = ", ".join(paths[:limit])
    if len(paths) > limit:
        shown += f" (+{len(paths) - limit})"
    return shown


def describe(added: list[str], removed: list[str], changed: list[str], limit: int) -> str:
    lines = [
        f"[граф] РАСХОЖДЕНИЕ С КОДОМ: новых {len(added)}, "
        f"удалённых {len(removed)}, изменённых {len(changed)}. "
        f"По этим файлам граф недостоверен — читай их напрямую."
    ]
    if added:
        lines.append(f"  новые:      {render(added, limit)}")
    if removed:
        lines.append(f"  удалённые:  {render(removed, limit)}")
    if changed:
        lines.append(f"  изменённые: {render(changed, limit)}")
    lines.append(f"  {UPDATE_HINT}")
    return "\n".join(lines)


def compare(root: Path, known: dict[str, float], disk: set[str]) -> tuple[list, list, list]:
    added = sorted(disk - known.keys())
    removed = sorted(known.keys() - disk)
    changed = []
    for path in sorted(disk & known.keys()):
        try:
            mtime = (root / path).stat().st_mtime
        except OSError:
            continue
        if mtime > known[path] + MTIME_TOLERANCE_SEC:
            changed.append(path)
    return added, removed, changed


def health_report(root: Path) -> str | None:
    """Целостность самого графа: висячие концы, петли, дубликаты рёбер.

    Считается по graph.json средствами стандартной библиотеки, без импорта
    graphify: проверка обязана работать и в CI, где graphify не установлен.
    Значения сверены с `graphify diagnose multigraph --json` — совпадают.

    Граница: схлопывание параллельных рёбер здесь не видно. graph.json уже
    построен как простой граф, дубликаты в нём исчезли на этапе сборки; поймать
    их можно только по сырой выгрузке .graphify_extract.json, которая не хранится.

    Возвращает None, когда всё чисто. Находки не считаются поводом уронить
    сборку: граф остаётся пригодным, но дефект должен быть виден.
    """
    path = root / "graphify-out" / "graph.json"
    if not path.is_file():
        return None
    try:
        graph = json.loads(path.read_text(encoding="utf-8"))
        links = graph["links"]
        ids = {node.get("id") for node in graph["nodes"]}
    except (OSError, ValueError, KeyError, TypeError):
        return "[граф] graph.json нечитаем или неожиданной структуры — целостность не проверена."

    missing = dangling = loops = 0
    pairs = set()
    duplicates = 0
    for link in links:
        source, target = link.get("source"), link.get("target")
        if source is None or target is None:
            missing += 1
            continue
        if source not in ids or target not in ids:
            dangling += 1
        if source == target:
            loops += 1
        key = (source, target)
        if key in pairs:
            duplicates += 1
        pairs.add(key)

    # Порядок «существительное: число» выбран, чтобы не согласовывать падеж
    # с произвольным числом («1 петель» против «1 петля»).
    flags = [
        f"{label}: {count}"
        for count, label in (
            (missing, "рёбер без конца"),
            (dangling, "рёбер в несуществующий узел"),
            (loops, "петель"),
            (duplicates, "дублирующихся рёбер"),
        )
        if count
    ]
    if not flags:
        return None
    return ("[граф] ЦЕЛОСТНОСТЬ: " + "; ".join(flags) +
            ". Граф пригоден, но дефект нужно показать пользователю, а не скрыть.")


def emit_hook(event: str, message: str | None) -> int:
    """Хук всегда завершается нулём: его дело — сообщить, а не заблокировать работу."""
    if message:
        json.dump({
            "hookSpecificOutput": {
                "hookEventName": event,
                "additionalContext": message,
            },
            "suppressOutput": True,
        }, sys.stdout, ensure_ascii=False)
    return 0


def read_hook_stdin() -> dict:
    try:
        raw = sys.stdin.read()
    except (OSError, ValueError):
        return {}
    try:
        return json.loads(raw) if raw.strip() else {}
    except ValueError:
        return {}


def written_path(root: Path, payload: dict) -> str | None:
    """Путь, только что записанный инструментом, в виде относительного к корню."""
    for source in (payload.get("tool_response"), payload.get("tool_input")):
        if not isinstance(source, dict):
            continue
        for key in ("filePath", "file_path"):
            value = source.get(key)
            if isinstance(value, str) and value:
                try:
                    return Path(value).resolve().relative_to(root.resolve()).as_posix()
                except (ValueError, OSError):
                    return None
    return None


def run_ci(root: Path, known: dict[str, float], limit: int) -> int:
    """Гейт для CI: состав корпуса плюс сравнение времени коммитов.

    mtime после checkout недостоверны, поэтому «изменённые» определяются не по ним,
    а по тому, что последний коммит с правкой корпуса новее последнего коммита,
    тронувшего граф.
    """
    disk = corpus_on_disk(root)
    if disk is None:
        print("[граф] не удалось перечислить корпус — проверка невозможна.", file=sys.stderr)
        return 2

    added = sorted(disk - known.keys())
    removed = sorted(known.keys() - disk)

    graph_ts = (git(root, "log", "-1", "--format=%ct", "--", "graphify-out/graph.json") or "").strip()
    code_ts = (git(root, "log", "-1", "--format=%ct", "--",
                   "*.cs", "*.xaml", "*.csproj", "*.slnx") or "").strip()
    behind = bool(graph_ts and code_ts and int(code_ts) > int(graph_ts))

    # Целостность сообщается всегда, но не влияет на код возврата: дефект графа
    # надо видеть, а не превращать в блокировку сборки.
    health = health_report(root)

    if not (added or removed or behind):
        print(f"[граф] актуален: {len(disk)} файлов покрыто.")
        if health:
            print(health)
        return 0

    if added or removed:
        print(describe(added, removed, [], limit))
    if behind:
        print("[граф] последний коммит с правкой кода новее последнего коммита с графом — "
              f"граф не пересобран после изменений. {UPDATE_HINT}")
    if health:
        print(health)
    return 1


def main() -> int:
    force_utf8_streams()
    parser = argparse.ArgumentParser(add_help=True)
    parser.add_argument("--hook", choices=["SessionStart", "PostToolUse"],
                        help="вывести JSON для хука Claude Code и всегда вернуть 0")
    parser.add_argument("--ci", action="store_true",
                        help="режим гейта: состав корпуса и время коммитов вместо mtime")
    parser.add_argument("--quiet", action="store_true",
                        help="молчать, когда граф актуален")
    parser.add_argument("--limit", type=int, default=10,
                        help="сколько путей показывать в каждой категории")
    args = parser.parse_args()

    root = repo_root()
    known = load_manifest(root)
    if isinstance(known, int):
        message = ("[граф] graphify-out/manifest.json отсутствует или нечитаем — "
                   "граф недостоверен. Пересобери: /graphify .")
        if args.hook:
            return emit_hook(args.hook, message)
        print(message)
        return 2

    if args.ci:
        return run_ci(root, known, args.limit)

    # PostToolUse: интересует ровно один случай — записан файл, которого граф
    # никогда не видел. Он и делает модуль невидимым для последующих вопросов.
    # Правки уже известных файлов сюда намеренно не попадают: иначе после первой
    # же правки предупреждение повторялось бы на каждой записи до конца сессии.
    if args.hook == "PostToolUse":
        path = written_path(root, read_hook_stdin())
        if not path or not is_corpus_path(path) or path in known:
            return emit_hook(args.hook, None)
        return emit_hook(args.hook, f"[граф] новый файл {path} не покрыт графом — "
                                    f"о нём граф ответить не сможет. {UPDATE_HINT}")

    disk = corpus_on_disk(root)
    if disk is None:
        message = "[граф] не удалось перечислить корпус — проверка пропущена."
        if args.hook:
            return emit_hook(args.hook, None)
        print(message, file=sys.stderr)
        return 0

    added, removed, changed = compare(root, known, disk)
    health = health_report(root)

    if not (added or removed or changed):
        if args.hook:
            return emit_hook(args.hook, health)
        if not args.quiet:
            print(f"[граф] актуален: {len(disk)} файлов покрыто.")
        if health:
            print(health)
        return 0

    message = describe(added, removed, changed, args.limit)
    if health:
        message += "\n" + health
    if args.hook:
        return emit_hook(args.hook, message)
    print(message)
    return 1


if __name__ == "__main__":
    sys.exit(main())

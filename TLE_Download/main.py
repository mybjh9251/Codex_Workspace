from __future__ import annotations

import argparse
import sys
from collections.abc import Sequence
from pathlib import Path


def bootstrap(argv: Sequence[str] | None = None) -> int:
    args = _parse_args(argv)
    project_root = _get_project_root()

    if not getattr(sys, "frozen", False):
        src_dir = project_root / "src"
        if str(src_dir) not in sys.path:
            sys.path.insert(0, str(src_dir))

    from app import run

    return run(project_root, input_path=args.input_path)


def _parse_args(argv: Sequence[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Download TLE data from Sat_List.xlsx or Sat_List.csv."
    )
    parser.add_argument(
        "-i",
        "--input",
        dest="input_path",
        type=Path,
        help=(
            "Optional satellite input file. If omitted, Sat_List.xlsx is used first "
            "and Sat_List.csv is used only when the xlsx file is missing."
        ),
    )
    return parser.parse_args(argv)


def _get_project_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


if __name__ == "__main__":
    raise SystemExit(bootstrap(sys.argv[1:]))

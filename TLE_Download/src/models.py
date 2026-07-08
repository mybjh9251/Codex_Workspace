from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class SatelliteRequest:
    row_number: int
    sat_name: str
    norad_id: str


@dataclass(frozen=True)
class TleLines:
    line1: str
    line2: str
    source: str


@dataclass(frozen=True)
class TleRecord:
    sat_name: str
    norad_id: str
    line1: str
    line2: str
    source: str


@dataclass(frozen=True)
class SpaceTrackCredentials:
    identity: str
    password: str


@dataclass(frozen=True)
class AppConfig:
    project_root: Path
    input_satellite_path: Path
    profile_path: Path
    log_dir: Path
    sheet_name: str
    sat_name_header: str
    norad_id_header: str
    request_timeout_seconds: int
    user_agent: str

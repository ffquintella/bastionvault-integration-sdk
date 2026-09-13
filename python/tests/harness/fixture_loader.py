"""Repository-backed fixture discovery and Draft 2020-12 validation."""

from __future__ import annotations

import json
from collections.abc import Collection
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError


class FixtureRepositoryNotFoundError(FileNotFoundError):
    """Raised when a test cannot locate the repository fixture schema."""


class FixtureNotFoundError(FileNotFoundError):
    """Raised when a requested fixture id is not present in the repository."""


class FixtureValidationError(ValueError):
    """Raised when a fixture violates the repository schema."""


class FixtureLoader:
    """Load fixtures directly from the repository at test time.

    The loader deliberately keeps only paths and parsed JSON in memory. It does
    not copy, embed, vendor, or generate fixture files.
    """

    _schema_relative_path = Path("specifications") / "fixtures" / "schema" / "fixture.schema.json"

    def __init__(self, start_path: Path | None = None) -> None:
        origin = (start_path or Path(__file__)).resolve()
        if origin.is_file():
            origin = origin.parent
        self.repository_root = self._find_repository_root(origin)
        self.fixtures_root = self.repository_root / "specifications" / "fixtures"
        self.schema_path = self.repository_root / self._schema_relative_path
        try:
            self.schema = json.loads(self.schema_path.read_text(encoding="utf-8"))
            self.validator = Draft202012Validator(self.schema)
        except (OSError, json.JSONDecodeError, SchemaError) as error:
            raise FixtureRepositoryNotFoundError(
                f"Could not load fixture schema at '{self.schema_path}': {error}"
            ) from error

    @classmethod
    def _find_repository_root(cls, origin: Path) -> Path:
        for candidate in (origin, *origin.parents):
            schema_path = candidate / cls._schema_relative_path
            if schema_path.is_file():
                return candidate
        relative = str(cls._schema_relative_path).replace("\\", "/")
        raise FixtureRepositoryNotFoundError(
            f"Could not locate repository root from '{origin}'. "
            f"Expected '{relative}'."
        )

    def enumerate_fixtures(self) -> list[dict[str, Any]]:
        """Return every non-schema JSON fixture in deterministic path order."""
        paths = sorted(
            path
            for path in self.fixtures_root.rglob("*.json")
            if "schema" not in path.relative_to(self.fixtures_root).parts
        )
        return [self._load_path(path) for path in paths]

    def filter_fixtures(
        self,
        *,
        level: str | None = None,
        sections: Collection[str] | None = None,
    ) -> list[dict[str, Any]]:
        """Filter validated fixtures by level and required section membership."""
        required_sections = set(sections) if sections is not None else None
        selected: list[dict[str, Any]] = []
        for fixture in self.enumerate_fixtures():
            if level is not None and fixture.get("level") != level:
                continue
            fixture_sections = set(fixture.get("sections", []))
            if required_sections is not None and not required_sections.issubset(fixture_sections):
                continue
            selected.append(fixture)
        return selected

    def load_fixture(self, fixture_id: str) -> dict[str, Any]:
        """Load and validate one fixture by its id."""
        matching_paths = sorted(self.fixtures_root.rglob(f"{fixture_id}.json"))
        for path in matching_paths:
            if "schema" not in path.relative_to(self.fixtures_root).parts:
                fixture = self._load_path(path)
                if fixture.get("id") == fixture_id:
                    return fixture
        for path in sorted(self.fixtures_root.rglob("*.json")):
            if "schema" in path.relative_to(self.fixtures_root).parts:
                continue
            fixture = self._load_path(path)
            if fixture.get("id") == fixture_id:
                return fixture
        raise FixtureNotFoundError(f"Fixture id '{fixture_id}' was not found under '{self.fixtures_root}'.")

    def validate_fixture(self, fixture: dict[str, Any]) -> None:
        """Validate a parsed fixture and report its id and violated constraint."""
        fixture_id = fixture.get("id", "<unknown>")
        error = next(self.validator.iter_errors(fixture), None)
        if error is None:
            return
        location = ".".join(str(part) for part in error.absolute_path) or "$"
        raise FixtureValidationError(
            f"Fixture '{fixture_id}' failed schema validation at {location}: {error.message}"
        ) from error

    def _load_path(self, path: Path) -> dict[str, Any]:
        try:
            fixture = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise FixtureValidationError(f"Fixture file '{path}' could not be loaded: {error}") from error
        if not isinstance(fixture, dict):
            raise FixtureValidationError("Fixture '<unknown>' failed schema validation: expected an object")
        self.validate_fixture(fixture)
        return fixture

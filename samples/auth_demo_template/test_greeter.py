"""Baseline tests the Tester worker runs BEFORE any decision (independent work)."""
import pytest

from greeter import greet, login_banner


def test_greet_basic():
    assert greet("Ada") == "Hello, Ada!"


def test_greet_requires_name():
    with pytest.raises(ValueError):
        greet("")


def test_login_banner():
    assert "Sign in" in login_banner()

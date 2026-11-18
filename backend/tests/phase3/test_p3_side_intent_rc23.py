"""rc23 (live run, to-do module): "Developer, can you also let the user delete a task by its number?" was answered as a
status question, so no Apply change was offered. Feature-request verbs are change requests; read questions stay reads."""
from __future__ import annotations

import pytest

from app.phase3.side_intent import classify


@pytest.mark.parametrize("q", [
    "Developer, can you also let the user delete a task by its number?",
    "can you allow negative numbers",
    "please validate the email too",
    "could you support dark mode",
    "Developer, can you also let the user choose the character instead of a star?",
    "Developer, can you change the password minimum to 12 characters?",
])
def test_feature_requests_are_change_requests(q):
    assert classify(q).is_change, q


@pytest.mark.parametrize("q", [
    "Developer, can you show me the test results?",
    "do you support unicode?",
    "is the developer handling the login?",
    "what are you working on?",
    "can you tell me why it failed?",
    "how is it going",
])
def test_read_questions_stay_reads(q):
    assert not classify(q).is_change, q

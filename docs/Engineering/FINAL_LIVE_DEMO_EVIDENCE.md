# Final live demo evidence

Status: **LIVE backend PASS** (13/13) on 2026-11-16 23:09, commit `7220f0a`, rc17 code, Meta model.
**The same flow from the Quest UI is PENDING the owner** (`RC17_FINAL_HARDWARE_QA.md` §E).

- Command: `cd backend; .venv\Scripts\python.exe -I scripts\live_real_work.py http://127.0.0.1:8765 ..\Logs\live\final_live_evidence.json`
- Raw evidence: `Logs/live/final_live_evidence.json` (every request and response) and `Logs/live/final_live_evidence.md` (copied below).
- Folder: `backend/sandbox_workspaces/plans/mp-5f997c5bbd`. It is git-ignored local output: keep it until filming is done
  (shot 8 can show it).

## What this run proves

| Requirement (brief §9) | Evidence |
|---|---|
| Objective submitted; the Meta-backed Manager plans; schema validated | `mp-5f997c5bbd`, provenance LIVE_MODEL, 6 steps, `work_execution: sandbox` (17 s) |
| Review → approval | The commit created 6 cards; a replay creates nothing (the journey suite proves approve-once separately: `live_journey.py` 17/17) |
| Real files, real pytest, visible fixes | Developer cards: 7 → 13 passed (**after 1 real fix**) → 40; Tester cards 24 and 36 passed |
| Artifacts stored; status reaches the client | Each card's result row below is the backend's plan status, the same data the Quest status page reads |
| Independent verification | This script ran pytest on the folder itself: **40 passed** before Apply change, **32 passed** after it, equal to the last card each time |
| Read-only question | "Developer, what have you changed?" was answered from the record and named real files; the plan was byte-identical before and after |
| Change request | "Developer, can you change the password minimum to 12 characters?" returned a suggestion marked *Requested change (not applied)*; nothing changed |
| Apply change | ONE new step, `t2-4895f40b48` "Change: the password minimum to 12 characters"; pressing again created nothing; worked for real in **180 s**, **2 real fix attempts**, final **pytest 32 passed, 0 failed** |
| The change is real | `login_validation.py`: `PASSWORD_MIN_LENGTH = 12`; the new `test_password_min12.py` asserts an 11-char and the old 8-char password are now rejected |
| Durations | Plan 17 s; 6 cards 300 s; Apply-change step 180 s. The film must say "minutes later" |

**Why there are 32 tests after the change, not 40:** the change step rewrote the tests that asserted the old 8-character rule.
The count went down because those tests were replaced, not because anything failed.

## Security position (brief §9 "Security requirement")

| Question | Answer today |
|---|---|
| Is there a VM or container on this PC? | No Docker. WSL 2 with Ubuntu exists. Windows 11 Home has no Windows Sandbox. |
| What protects the PC now? | The model's code is static-checked before ANY run: an allow-list of harmless standard modules; no process, file, network or reflection builtins or dunders; nine more refused routes (importorskip, fs/import fixtures, attrgetter, Formatter.get_field, get_type_hints, string skipif/xfail, pytest_plugins, std-module file names, unittest.mock). Refused files are never written. Files go only into the plan folder (path checks). pytest runs as `python -I -B` with plugin auto-load off, a timeout, and credentials removed from its environment. |
| Is that an OS sandbox? | **No.** A determined attacker could find a gap in a static filter, and the run has the same Windows permissions and network as the backend. |
| Residual risk accepted for the demo | The model is asked for small login-validation modules, and its output is checked and readable in the folder. Run the live demo only on this PC, with no secrets in the user profile beyond what is already there (the API key lives in `backend/.env`, which the child process cannot see in its env, but a malicious file read could reach the disk). |
| Next step (post-deadline) | Run pytest inside WSL 2 with `unshare -rmn`: a separate Linux VM, no network namespace, Windows drives hidden by a tmpfs over `/mnt`, the workspace copied to `/tmp`. Not done now: it needs pytest installed in the WSL distro, about 1 GB RAM (the PC had 0.5 GB free), and testing time the deadline does not allow. |

**Wording for the submission:** "a restricted workspace on the developer's PC". Never "sandboxed" or "isolated".

---
# Live run evidence 2026-11-16 23:09

- backend: http://127.0.0.1:8765 (Meta model; MUSEDESK_P3_EXECUTE_CODE on)
- objective: Build me a login page with email validation, password requirements, and tests.
- plan: `mp-5f997c5bbd` (LIVE_MODEL), 6 steps

| card | agent | state | recorded result |
|---|---|---|---|
| `t2-dcbc6b84c5` Research email and password validation standards | researcher | completed | Research write-up: Recommend simple normalized email check, NIST-based 8-plus character password rules, and accessible blur-plus-submit inline errors for the lo |
| `t2-449dc20a4f` Build login page layout with email and password fields | developer | completed | Code written: login_page.py, login.html, test_login_page.py · pytest 7 passed, 0 failed |
| `t2-163861d4ef` Implement email validation and password requirement checks | developer | completed | Code written: login_validation.py, test_login_validation.py · pytest 13 passed, 0 failed (after 1 fix) |
| `t2-6427b83195` Add unit tests for email and password validation | tester | completed | Tests run: 24 passed, 0 failed |
| `t2-1f18073c89` Verify login form behavior and error messaging | tester | completed | Tests run: 36 passed, 0 failed |
| `t2-82dcf65b77` Document validation rules and test instructions | developer | completed | Code written: validation_docs.py, VALIDATION_RULES.md, test_validation_docs.py · pytest 40 passed, 0 failed |
| `t2-4895f40b48` Change: the password minimum to 12 characters | developer | completed | Code written: VALIDATION_RULES.md, login.html, login_page.py, login_validation.py, test_login_behavior.py, test_login_validation.py, test_password_min12.py, tes |

## Files in `sandbox_workspaces/plans/mp-5f997c5bbd`

- `login.html` (2997 bytes)
- `login_page.py` (4309 bytes)
- `login_validation.py` (3929 bytes)
- `pytest.ini` (62 bytes)
- `test_login_behavior.py` (2236 bytes)
- `test_login_page.py` (2201 bytes)
- `test_login_validation.py` (3624 bytes)
- `test_password_min12.py` (2108 bytes)
- `test_validation_docs.py` (1712 bytes)
- `validation_docs.py` (1447 bytes)
- `VALIDATION_RULES.md` (858 bytes)

### `login_page.py` (first 40 lines, verbatim)

```python
"""Render an accessible, responsive login page layout."""

from typing import Dict

PAGE_TITLE = "Log in"

CSS = (
    ".login-page{min-height:100vh;margin:0;display:flex;align-items:center;"
    "justify-content:center;background:#f4f5f7;font-family:system-ui,sans-serif;"
    "padding:1rem;box-sizing:border-box}"
    ".login-card{width:100%;max-width:26rem;background:#fff;border-radius:0.75rem;"
    "padding:2rem;box-shadow:0 0.25rem 1rem rgba(0,0,0,0.08);box-sizing:border-box}"
    ".login-card h1{margin:0 0 0.25rem;font-size:1.5rem}"
    ".subtitle{margin:0 0 1.5rem;color:#555}"
    ".field{margin-bottom:1rem}"
    ".field label{display:block;font-weight:600;margin-bottom:0.375rem}"
    ".field input{width:100%;padding:0.625rem 0.75rem;font-size:1rem;"
    "border:1px solid #767676;border-radius:0.375rem;box-sizing:border-box}"
    ".field input:focus-visible{outline:3px solid #005fcc;outline-offset:1px}"
    ".password-wrap{display:flex;gap:0.5rem}"
    ".password-wrap input{flex:1;min-width:0}"
    ".hint{font-size:0.875rem;color:#444;margin:0.375rem 0 0}"
    ".error{font-size:0.875rem;color:#b00020;margin:0.375rem 0 0;min-height:1.25em}"
    ".form-error{margin:0 0 1rem;font-weight:600}"
    "button[type=submit]{width:100%;padding:0.75rem;font-size:1rem;font-weight:700;"
    "color:#fff;background:#005fcc;border:0;border-radius:0.375rem;cursor:pointer}"
    "button[type=submit]:hover{background:#004bb5}"
    "button[type=submit]:focus-visible{outline:3px solid #00397a;outline-offset:2px}"
    "#toggle-password{flex-shrink:0;padding:0 0.75rem;border:1px solid #767676;"
    "border-radius:0.375rem;background:#fff;cursor:pointer}"
    "@media (max-width:30rem){.login-card{padding:1.25rem}.login-page{padding:0.5rem;align-items:flex-start}}"
)


def render_login_page() -> str:
    """Return the full HTML page for the login layout."""
    return (
        "<!DOCTYPE html>\n"
        '<html lang="en">\n'
        "<head>\n"
```

## Independent pytest run of the folder (this script, after the run)

```
........................................                                 [100%]
40 passed in 0.11s
```

## Apply change

- suggestion: Requested change (not applied): Developer, can you change the password minimum to 12 characters?
- new step: `['t2-4895f40b48']` -> completed: Code written: VALIDATION_RULES.md, login.html, login_page.py, login_validation.py, test_login_behavior.py, test_login_validation.py, test_password_min12.py, test_validation_docs.py, validation_docs.py
- seconds from Apply to the end of the step: 180

Checks: 13/13 passed.

(After Apply change, the independent re-run of the folder: **32 passed**, equal to the change card's "pytest 32 passed, 0 failed (after 2 fixes)".)

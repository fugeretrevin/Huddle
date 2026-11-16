"""One cheap live request to the Meta Model API. Prints only status/model/first 80 chars.

Key comes from env MUSE_LLM_API_KEY / LLAMA_API_KEY / MUSEDESK_PROVIDER_API_KEY, or backend/.env.
Usage: python scripts/meta_smoke.py [base_url] [model]
"""
import os
import pathlib
import sys

import httpx

base = sys.argv[1] if len(sys.argv) > 1 else "https://api.meta.ai/v1"
model = sys.argv[2] if len(sys.argv) > 2 else "muse-spark-1.3"
key = ""
envf = pathlib.Path(__file__).resolve().parents[1] / ".env"
vals = {}
if envf.exists():
    for line in envf.read_text(encoding="utf-8").splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            k, v = line.split("=", 1)
            vals[k.strip()] = v.strip().strip('"').strip("'")
for name in ("MUSEDESK_PROVIDER_API_KEY", "MUSE_LLM_API_KEY", "LLAMA_API_KEY"):
    key = key or os.environ.get(name) or vals.get(name, "")
if not key:
    sys.exit("no key")
import ssl
r = httpx.post(base.rstrip("/") + "/chat/completions", timeout=60, verify=ssl.create_default_context(),
               headers={"Authorization": f"Bearer {key}"},
               json={"model": model, "max_completion_tokens": 2000, "reasoning_effort": "low",
                     "messages": [{"role": "user", "content": "Reply with the word OK."}]})
print("status", r.status_code)
try:
    j = r.json()
    if r.status_code == 200:
        print("model", j.get("model"), "reply", repr(j["choices"][0]["message"]["content"][:80]))
    else:
        err = j.get("error", j)
        print("error", str(err).replace(key, "<key>")[:300])
except Exception as e:  # noqa: BLE001
    print("unparsed", type(e).__name__, r.text[:100].replace(key, "<key>"))

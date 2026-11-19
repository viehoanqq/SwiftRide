"""Build a git-ignored Postman environment with local Admin credentials."""

import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
dotenv = {}
for line in (root / ".env").read_text(encoding="utf-8").splitlines():
    line = line.strip()
    if not line or line.startswith("#") or "=" not in line:
        continue
    key, value = line.split("=", 1)
    dotenv[key.strip()] = value.strip().strip("'\"")

phone = dotenv.get("Identity__AdminBootstrap__Phone")
password = dotenv.get("Identity__AdminBootstrap__Password")
if not phone or not password:
    raise SystemExit("Set Identity__AdminBootstrap__Phone and Password in .env first.")

environment = json.loads(
    (root / "postman/SwiftRide-Gateway.postman_environment.json").read_text(encoding="utf-8")
)
environment["name"] = "SwiftRide - API Gateway (Local private)"
environment["id"] = "8b7d9a2e-4f1c-4e9b-9c3a-1a2b3c4d5e70"
for variable in environment["values"]:
    if variable["key"] == "adminPhone":
        variable["value"] = phone
    elif variable["key"] == "adminPassword":
        variable["value"] = password
        variable["type"] = "secret"

output = root / "postman/SwiftRide-Gateway.local.postman_environment.json"
output.write_text(json.dumps(environment, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(output)

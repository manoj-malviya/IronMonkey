SHELL := /bin/bash
.DEFAULT_GOAL := help

# Aspire orchestrates PostgreSQL + apiservice. The Blazor frontend is started
# separately because `.WaitFor(apiService)` in AppHost.cs prevents Aspire from ever
# launching the webfrontend resource (see `make doctor` and README notes).
SOLUTION      := IronMonkey.sln
APPHOST       := IronMonkey.AppHost
APISERVICE    := IronMonkey.ApiService
WEB           := IronMonkey.Web
TESTS         := IronMonkey.Tests

API_URL       := https://localhost:7306
DASHBOARD_URL := https://localhost:17019
WEB_PORT      := 5299
WEB_URL       := http://localhost:$(WEB_PORT)

RUN_DIR       := .run
APPHOST_LOG   := $(RUN_DIR)/apphost.log
WEB_LOG       := $(RUN_DIR)/web.log
APPHOST_PID   := $(RUN_DIR)/apphost.pid
WEB_PID       := $(RUN_DIR)/web.pid

# Waits, in seconds. Cold start creates the database and applies migrations.
API_TIMEOUT   := 180
WEB_TIMEOUT   := 90

.PHONY: help up down restart start-api start-web stop-api stop-web status logs \
        logs-api logs-web build test clean doctor migrate psql urls

help: ## Show available targets
	@echo "IronMonkey — application control"
	@echo
	@grep -hE '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) \
	  | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'
	@echo
	@echo "Quick start:  make up     (then: make status, make down)"

up: start-api start-web urls ## Start everything (PostgreSQL + API + Blazor web)

down: stop-web stop-api ## Stop everything and remove the PostgreSQL container

restart: down up ## Full restart

urls: ## Print service URLs
	@echo
	@echo "  Web app    $(WEB_URL)"
	@echo "  API        $(API_URL)"
	@echo "  Dashboard  $(DASHBOARD_URL)  (token is in $(APPHOST_LOG))"

$(RUN_DIR):
	@mkdir -p $(RUN_DIR)

build: ## Build the solution
	@dotnet build $(SOLUTION) --nologo -v q

start-api: | $(RUN_DIR) ## Start Aspire (PostgreSQL + API) and wait until healthy
	@# Kept as one recipe line so the "already running" branch genuinely skips the launch.
	@if [ -f $(APPHOST_PID) ] && kill -0 $$(cat $(APPHOST_PID)) 2>/dev/null; then \
	  echo "Aspire already running (pid $$(cat $(APPHOST_PID))) — $(API_URL)"; \
	else \
	  docker info >/dev/null 2>&1 || { echo "ERROR: docker daemon is not reachable (required for PostgreSQL)"; exit 1; }; \
	  echo "Starting Aspire (PostgreSQL + API)..."; \
	  ASPIRE_ALLOW_UNSECURED_TRANSPORT=true DOTNET_ENVIRONMENT=Development \
	    setsid nohup dotnet run --project $(APPHOST) > $(APPHOST_LOG) 2>&1 < /dev/null & \
	  echo $$! > $(APPHOST_PID); \
	  printf "Waiting for API health"; \
	  for i in $$(seq 1 $(API_TIMEOUT)); do \
	    code=$$(curl -sk -m 3 -o /dev/null -w '%{http_code}' $(API_URL)/health 2>/dev/null); \
	    if [ "$$code" = "200" ]; then echo " ok ($${i}s)"; break; fi; \
	    if ! kill -0 $$(cat $(APPHOST_PID)) 2>/dev/null; then \
	      echo " FAILED (process exited)"; tail -25 $(APPHOST_LOG); exit 1; fi; \
	    if [ $$i = $(API_TIMEOUT) ]; then \
	      echo " TIMEOUT after $(API_TIMEOUT)s"; tail -25 $(APPHOST_LOG); exit 1; fi; \
	    printf "."; sleep 1; \
	  done; \
	fi

start-web: | $(RUN_DIR) ## Start the Blazor frontend (requires a healthy API)
	@# Single recipe line: an early `exit 0` in a separate line would only end that
	@# line's shell, and the launch below would still run and start a duplicate.
	@if [ -f $(WEB_PID) ] && kill -0 $$(cat $(WEB_PID)) 2>/dev/null; then \
	  echo "Web already running (pid $$(cat $(WEB_PID))) — $(WEB_URL)"; \
	else \
	  code=$$(curl -sk -m 5 -o /dev/null -w '%{http_code}' $(API_URL)/health 2>/dev/null); \
	  if [ "$$code" != "200" ]; then \
	    echo "ERROR: API is not healthy at $(API_URL) — run 'make start-api' first"; exit 1; fi; \
	  echo "Starting Blazor frontend..."; \
	  ASPNETCORE_ENVIRONMENT=Development \
	  ASPNETCORE_URLS=$(WEB_URL) \
	  services__apiservice__https__0=$(API_URL) \
	    setsid nohup dotnet run --project $(WEB) --no-launch-profile > $(WEB_LOG) 2>&1 < /dev/null & \
	  echo $$! > $(WEB_PID); \
	  printf "Waiting for web"; \
	  for i in $$(seq 1 $(WEB_TIMEOUT)); do \
	    code=$$(curl -s -m 3 -o /dev/null -w '%{http_code}' $(WEB_URL)/ 2>/dev/null); \
	    if [ "$$code" = "200" ]; then echo " ok ($${i}s)"; break; fi; \
	    if ! kill -0 $$(cat $(WEB_PID)) 2>/dev/null; then \
	      echo " FAILED (process exited)"; tail -25 $(WEB_LOG); exit 1; fi; \
	    if [ $$i = $(WEB_TIMEOUT) ]; then \
	      echo " TIMEOUT after $(WEB_TIMEOUT)s"; tail -25 $(WEB_LOG); exit 1; fi; \
	    printf "."; sleep 1; \
	  done; \
	fi

stop-web: ## Stop the Blazor frontend
	@if [ -f $(WEB_PID) ]; then kill -- -$$(cat $(WEB_PID)) 2>/dev/null || kill $$(cat $(WEB_PID)) 2>/dev/null || true; rm -f $(WEB_PID); fi
	@pkill -f '$(WEB)/bin/.*/$(WEB)$$' 2>/dev/null || true
	@echo "Web stopped."

stop-api: ## Stop Aspire and remove its PostgreSQL container
	@if [ -f $(APPHOST_PID) ]; then kill -- -$$(cat $(APPHOST_PID)) 2>/dev/null || kill $$(cat $(APPHOST_PID)) 2>/dev/null || true; rm -f $(APPHOST_PID); fi
	@pkill -f '$(APPHOST)/bin/.*/$(APPHOST)$$' 2>/dev/null || true
	@pkill -f '$(APISERVICE)/bin/.*/$(APISERVICE)$$' 2>/dev/null || true
	@sleep 2
	@for c in $$(docker ps -aq --filter 'name=postgres-' 2>/dev/null); do docker rm -f $$c >/dev/null 2>&1 || true; done
	@echo "API and PostgreSQL stopped."

status: ## Show what is running
	@printf '  %-12s ' "Aspire:"; \
	  if pgrep -f '$(APPHOST)/bin/.*/$(APPHOST)$$' >/dev/null 2>&1; then echo "running"; else echo "stopped"; fi
	@printf '  %-12s ' "API:"; \
	  code=$$(curl -sk -m 4 -o /dev/null -w '%{http_code}' $(API_URL)/health 2>/dev/null); \
	  if [ "$$code" = "200" ]; then echo "healthy ($(API_URL))"; else echo "down (health=$${code:-000})"; fi
	@printf '  %-12s ' "Web:"; \
	  code=$$(curl -s -m 4 -o /dev/null -w '%{http_code}' $(WEB_URL)/ 2>/dev/null); \
	  if [ "$$code" = "200" ]; then echo "up ($(WEB_URL))"; else echo "down (http=$${code:-000})"; fi
	@printf '  %-12s ' "Postgres:"; \
	  name=$$(docker ps --format '{{.Names}}' 2>/dev/null | grep '^postgres-' | head -1); \
	  if [ -n "$$name" ]; then echo "$$name $$(docker port $$name 5432/tcp 2>/dev/null | head -1)"; else echo "not running"; fi

logs: ## Tail both logs
	@tail -f $(APPHOST_LOG) $(WEB_LOG)

logs-api: ## Tail the Aspire/API log
	@tail -f $(APPHOST_LOG)

logs-web: ## Tail the web log
	@tail -f $(WEB_LOG)

test: ## Run the test suite (needs Docker for Testcontainers)
	@dotnet test $(TESTS) --nologo -v q

psql: ## Open psql against the running central database
	@name=$$(docker ps --format '{{.Names}}' | grep '^postgres-' | head -1); \
	  [ -n "$$name" ] || { echo "ERROR: no PostgreSQL container running"; exit 1; }; \
	  pw=$$(docker inspect $$name --format '{{range .Config.Env}}{{println .}}{{end}}' | grep POSTGRES_PASSWORD | cut -d= -f2); \
	  docker exec -it -e PGPASSWORD="$$pw" $$name psql -U postgres -d CentralDb

migrate: ## Apply central-DB migrations manually (the API also does this at startup)
	@name=$$(docker ps --format '{{.Names}}' | grep '^postgres-' | head -1); \
	  [ -n "$$name" ] || { echo "ERROR: no PostgreSQL container running — run 'make start-api'"; exit 1; }; \
	  port=$$(docker port $$name 5432/tcp | head -1 | sed 's/.*://'); \
	  pw=$$(docker inspect $$name --format '{{range .Config.Env}}{{println .}}{{end}}' | grep POSTGRES_PASSWORD | cut -d= -f2); \
	  dotnet ef database update --project IronMonkey.Data --startup-project $(APISERVICE) \
	    --context CentralDbContext \
	    --connection "Host=localhost;Port=$$port;Database=CentralDb;Username=postgres;Password=$$pw"

doctor: ## Check prerequisites and report known issues
	@echo "Prerequisites:"
	@printf '  %-12s ' "dotnet:"; dotnet --version 2>/dev/null || echo "MISSING"
	@printf '  %-12s ' "docker:"; \
	  if docker info >/dev/null 2>&1; then docker --version; else echo "NOT REACHABLE (required)"; fi
	@echo
	@echo "Known issues in this repo:"
	@echo "  - AppHost.cs uses .WaitFor(apiService) on the webfrontend resource. Aspire 13.1.0"
	@echo "    never launches it (DCP registers only postgres + apiservice), so 'make start-web'"
	@echo "    runs the Blazor app directly against $(API_URL)."
	@echo "  - The Web app trusts the dev certificate in Development only; 'dotnet dev-certs"
	@echo "    https --trust' hangs on Linux and is not needed."
	@echo "  - GET /roles returns 500: ListRoles uses the obsolete AppDbContext against the"
	@echo "    central DB, where the tenant-scoped Roles table does not exist."

clean: ## Stop everything, then remove build output and logs
	@$(MAKE) --no-print-directory down
	@dotnet clean $(SOLUTION) --nologo -v q 2>/dev/null || true
	@rm -rf $(RUN_DIR)
	@echo "Cleaned."

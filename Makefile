# Orion content-sync helpers.
#
# Upstream source of truth: Goob-Station/Goob-Station (remote `goob`).
# Ataraxia is archived and is NOT part of this pipeline.
#
# Flow:  make upstream         -> fetch goob, merge new commits into isolated `upst`
#        (build / test on upst)
#        make upstream-finish   -> move the tested result into master, delete `upst`

GOOB_REMOTE ?= goob
GOOB_URL    ?= https://github.com/Goob-Station/Goob-Station.git
GOOB_BRANCH ?= master
UPST_BRANCH ?= upst
BASE_BRANCH ?= master

.PHONY: upstream upstream-status upstream-finish upstream-abort ensure-remote

## ensure-remote: create/repoint the `goob` remote so this works on any clone
ensure-remote:
	@if git remote get-url $(GOOB_REMOTE) >/dev/null 2>&1; then \
		git remote set-url $(GOOB_REMOTE) $(GOOB_URL); \
	else \
		echo "+ adding remote '$(GOOB_REMOTE)' -> $(GOOB_URL)"; \
		git remote add $(GOOB_REMOTE) $(GOOB_URL); \
	fi

## upstream-status: fetch goob and report how far behind master is
upstream-status: ensure-remote
	@git fetch $(GOOB_REMOTE)
	@n=$$(git rev-list --count $(BASE_BRANCH)..$(GOOB_REMOTE)/$(GOOB_BRANCH)); \
	echo "$$n new commit(s) on $(GOOB_REMOTE)/$(GOOB_BRANCH) not in $(BASE_BRANCH)"

## upstream: fetch goob and merge new commits into an isolated `upst` branch
upstream: ensure-remote
	@git fetch $(GOOB_REMOTE)
	@if ! git diff --quiet || ! git diff --cached --quiet; then \
		echo "✗ Working tree has uncommitted changes — commit or stash first."; \
		exit 1; \
	fi
	@n=$$(git rev-list --count $(BASE_BRANCH)..$(GOOB_REMOTE)/$(GOOB_BRANCH)); \
	if [ "$$n" -eq 0 ]; then \
		echo "✓ Already up to date with $(GOOB_REMOTE)/$(GOOB_BRANCH)."; \
		exit 0; \
	fi; \
	echo "→ $$n new commit(s) from $(GOOB_REMOTE)/$(GOOB_BRANCH); staging into '$(UPST_BRANCH)'..."; \
	git switch -C $(UPST_BRANCH) $(BASE_BRANCH) && \
	if git merge $(GOOB_REMOTE)/$(GOOB_BRANCH); then \
		echo "✓ Merged cleanly into '$(UPST_BRANCH)'. Build/test, then: make upstream-finish"; \
	else \
		echo "⚠ Conflicts in '$(UPST_BRANCH)'. Resolve + commit, build/test, then: make upstream-finish"; \
		echo "  (to bail out entirely: make upstream-abort)"; \
		exit 1; \
	fi

## upstream-finish: move the tested `upst` branch into master and delete it
upstream-finish:
	@if git merge HEAD >/dev/null 2>&1; then :; else \
		echo "✗ A merge is still in progress — resolve and commit on '$(UPST_BRANCH)' first."; exit 1; \
	fi
	@git switch $(BASE_BRANCH)
	@git merge --ff-only $(UPST_BRANCH) 2>/dev/null || git merge --no-edit $(UPST_BRANCH)
	@git branch -d $(UPST_BRANCH) 2>/dev/null || true
	@echo "✓ '$(BASE_BRANCH)' updated. '$(UPST_BRANCH)' removed."

## upstream-abort: throw away an in-progress sync and return to master
upstream-abort:
	@git merge --abort 2>/dev/null || true
	@git switch $(BASE_BRANCH)
	@git branch -D $(UPST_BRANCH) 2>/dev/null || true
	@echo "✓ Aborted. Back on '$(BASE_BRANCH)', '$(UPST_BRANCH)' removed."

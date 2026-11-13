# AR_TEAM6 repository instructions

## Before editing

- Check the current branch, remotes, recent commits, and working tree with `git status --short --branch`, `git remote -v`, and `git log --oneline -5`.
- Preserve unrelated changes. If they overlap with the task or you find a conflict or unexpected remote history, explain the issue before proceeding with the affected work.
- Before editing, run `git pull --rebase` to synchronize the current branch with its remote upstream. Confirm the upstream and working-tree state first. If the pull cannot proceed safely, explain the blocker before editing.

## Branches and commits

- Use a separate branch for each independent change, such as `feature/<name>`, `fix/<name>`, `perf/<name>`, or `docs/<name>`. Fetch the remote and explicitly start the new branch from the latest remote commit of its intended merge target. For example, run `git fetch origin` and then `git switch --no-track -c docs/<name> origin/main` for a documentation change targeting `main`. Check an existing branch's state and ownership before reusing it.
- Commit each complete, verified change as a logical unit. Use Conventional Commits, such as `fix: prevent duplicate hit events`. For analysis or planning requests, leave the working tree and commit history untouched.
- Before committing, review `git status --short`, `git diff --stat`, and `git diff --check`. Stage only task-related paths and review the staged diff.
- Keep secrets, certificates, personal configuration, Unity-generated directories, and build artifacts out of commits. Preserve teammates' commits and public branch history. Avoid destructive commands such as `git reset --hard` and `git checkout -- <path>`.

## Verification

- Run the smallest relevant build, test, or static check before committing. Run `git diff --check` for Unity code changes and use Unity batch mode for the relevant compile or tests when the Editor is available.
- Report any relevant checks that could not run, including Unity Editor, device, performance, or integration tests.

## Pushes and pull requests

- Push or open a pull request only when the user explicitly requests that action. Use the feature branch for review and avoid merging unreviewed changes into `main`.
- After an authorized push, verify the branch status and the remote branch reference. Never force-push.
- In a pull request, describe the purpose, scope, key files, checks run, and any outstanding verification. When resolving conflicts, preserve teammates' valid changes and ask the user when the intended resolution is unclear.

## Completion report

- Summarize the changes, branch and commit hash when applicable, push or pull request status, checks run, and relevant checks still pending.

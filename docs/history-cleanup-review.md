# Historical database removal procedure for review

The active checkout ignores build output, SQLite databases, and logs. Older
commits can still contain copies. Treat historical database files, backups, and
clones as sensitive. The following procedure requires a separately approved
maintenance window.

1. Rotate any credentials that were present in the old database. Change the
   password in the active ServeX installation. If that password was reused
   elsewhere, rotate it there too.
2. Freeze pushes, resolve or document open PRs, and agree who can temporarily
   change branch protections. Inventory branch and tag refs and record their
   exact current IDs for lease checks. Identify any forks and offline clones.
3. In a fresh, access-controlled mirror, use `git filter-repo --analyze` to inspect
   historical file paths without opening the databases. Review the removal
   list with the repository owner. Include database files and their SQLite
   sidecars, logs, and tracked build directories. Account for renamed paths.
4. Rewrite the mirror locally with `git filter-repo --sensitive-data-removal`
   and the reviewed inverse path filters. Run the repository checks on the
   rewritten tips and verify every retained ref for residual sensitive paths.
   Keep the original refs and any recovery copy private.
5. Once explicitly approved, update only the inventoried branches and tags
   using force-with-lease against the recorded IDs. Stop if a lease fails.
   Restore protection rules and ask GitHub Support about cached PR refs or
   objects that are outside the repository owner's control.
6. Have collaborators re-clone. Existing clones, forks, caches, and previously
   downloaded files may retain exposed content. Reopen affected PRs and avoid
   pushing branches based on the old history.

The cleanup branch does not rewrite Git refs. Removal from history remains
open until the repository owner approves and verifies the maintenance.

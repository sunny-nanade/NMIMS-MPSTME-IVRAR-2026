# Rollback guide

Read this if a change broke something and you need to get back to a state that worked.

Every milestone in this project is pinned by an annotated tag named `checkpoint-NN-<name>`.
Any checkpoint can be restored in one command. Nothing is ever force-pushed, so no
checkpoint can be destroyed by later work.

## See what checkpoints exist

```
git tag -l 'checkpoint-*' -n1
```

The `-n1` prints each tag's description, so you can tell what state each one represents.

## Look at a checkpoint without changing anything

```
git show checkpoint-00-baseline --stat
git diff checkpoint-00-baseline --stat
```

The second one answers "what has changed since things last worked".

## Go back to a checkpoint

### Safe: look around, keep current work

```
git switch --detach checkpoint-00-baseline
```

Puts the working tree at that checkpoint without moving `main`. Nothing is lost.
Come back with:

```
git switch main
```

### Make main actually be that checkpoint again

```
git switch main
git reset --hard checkpoint-00-baseline
```

**This discards uncommitted work in the working tree.** Commits are not lost — they stay
reachable through the tags and through `git reflog`.

### Undo one bad commit but keep history honest

```
git revert <commit-sha>
```

Preferred when the branch is already pushed and someone else may have pulled it. Creates
a new commit that reverses the old one rather than rewriting shared history.

## After rolling back in Unity

Unity caches imported assets in `Library/`, which is gitignored and therefore not rolled
back with everything else. After any hard reset:

1. Close Unity **before** running the git command, if you can.
2. Reopen the project and let it reimport.
3. If things still look wrong, close Unity, delete the `Library/` folder, reopen. The
   reimport takes a long time on this project because of the vrbn assets, so try without
   deleting first.

## The absolute fallback

```
git switch --detach checkpoint-00-baseline
```

`checkpoint-00-baseline` is the project exactly as it was handed over, before any
spec-compliance rework. The branch `safety/pre-rework` points at the same commit as a
second anchor, so even if the tag were deleted the commit stays reachable.

## If your local copy is wrecked beyond repair

Checkpoints are pushed as well as kept locally, so you can always get a clean copy.
Clone the **fork** — that is where the checkpoints live:

```
git clone https://github.com/gandhialt/Acrophobia-Trainer.git
cd Acrophobia-Trainer
git switch --detach checkpoint-00-baseline
```

## Remotes

```
fork    gandhialt/Acrophobia-Trainer      writable  <- all work goes here
origin  Nightfury129/Acrophobia-Trainer   read-only <- upstream, owned by someone else
```

`main` tracks `fork/main`, so a bare `git push` goes to the fork. Origin's *push* URL is
deliberately set to a placeholder, because `gandhialt` is not a collaborator on it and
attempting to push triggers an endless Git Credential Manager login loop — GCM misreads
the permission error as a stale token and re-authenticates forever. Re-enable it only
once collaborator access is actually granted:

```
git remote set-url --push origin https://github.com/Nightfury129/Acrophobia-Trainer.git
```

Getting work into the upstream repo requires either that collaborator access or a pull
request from the fork. Forks cannot push upstream.

## Known noise in `git status`

Two things show as modified without anyone having edited them. Both are expected; leave
them alone.

- **20 `.tga` files under `Assets/vrbn_studios/.../B00906/Textures/`** — `.gitattributes`
  started tracking `*.tga` in Git LFS after those files were already committed as raw
  binary. The LFS clean filter now rewrites them to pointers on read, so git sees a diff
  against the raw blobs in history. No content changed. Fixing it properly means
  `git lfs migrate import --everything`, which rewrites history — not something to do
  before a deadline.

- **Unity settings assets** (`OculusProjectConfig.asset`,
  `UniversalRenderPipelineGlobalSettings.asset`, `ShaderGraphSettings.asset`) — Unity
  rewrote these with CRLF line endings on open. Byte-identical apart from line endings.

`Assets/XR/Settings/OpenXR Package Settings.asset` is a genuine change: Unity's OpenXR
package registered its Metro/UWP feature set on project open. Harmless for a Quest build.

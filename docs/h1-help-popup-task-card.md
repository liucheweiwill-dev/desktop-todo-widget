# H1 — Help popup

## Goal

Add a `?` button at the right end of the title bar. Clicking it opens a popup
containing a short usage guide. Closing it must not close the application.

## Files to edit

- `src/DesktopTodoWidget/App.xaml` — string resources only
- `src/DesktopTodoWidget/MainWindow.xaml` — `?` button + help popup
- `src/DesktopTodoWidget/MainWindow.xaml.cs` — click handler, drag exclusion, Escape rung

## Do not modify

- Anything under `Data/` or `Interop/`
- `tests/` — this change has no pure logic to test; verification is the manual list below
- The existing `⚙` popup and its handlers
- The first and last Escape rungs (cancel drag first, close window last)
- Any brush or colour value

## Requirements

**R1 — the button.** A `?` button at the right end of `WindowDragStrip`,
right-aligned, vertically centred, fitting inside the 24px row. Reuse
`TodoDeleteButtonStyle` with `Padding="5,0"`, matching `⚙` and `×`. Give it a
tooltip from a new string resource.

**R2 — the popup.** Clicking `?` opens a `Popup` anchored to the button,
`Placement="Bottom"`, `StaysOpen="False"` so clicking elsewhere dismisses it.
Chrome identical to the `⚙` popup: a `Border` with `MenuBackgroundBrush`
background, `DividerBrush` 1px border, `Padding="8"`.

**R3 — the button must actually receive its click.** `MainWindow_PreviewMouseLeftButtonDown`
currently starts a window drag for *anything* inside the strip and marks the
event handled:

```csharp
if (IsDescendantOf(source, WindowDragStrip))
{
    MoveWindow();
    e.Handled = true;
    return;
}
```

A `?` button added naively will therefore never fire its `Click`. Exclude the
help button (and the help popup) **before** this branch. The strip must still
drag the window everywhere else — this is the same code path that once made the
whole window undraggable, so it must be verified in both directions.

**R4 — Escape gains a fourth rung.** The new order, top to bottom:

| Priority | State | Escape does |
|---|---|---|
| 1 | Item drag in progress | cancel the drag |
| 2 | **Help popup open** | **close the help popup only** |
| 3 | A task is being edited | cancel the edit |
| 4 | otherwise | close the window |

Rungs 1 and 4 must keep their current behaviour. Getting rung 2 wrong closes the
application while the user is reading the help.

**R5 — the Popup keyboard trap.** A WPF `Popup` is hosted in its own HWND, so
key events raised while focus is inside it do not reach
`MainWindow_PreviewKeyDown`. Escape must still close the help popup. Either make
the popup non-focusable so focus never leaves the window, or handle
`PreviewKeyDown` on the popup content. **State in your report which you chose and
why.**

**R6 — strings.** Every UI string goes in `App.xaml` as a `sys:String` resource
referenced by `DynamicResource`, consistent with the existing entries. English
only. Use `xml:space="preserve"` for the multi-line bodies.

Resource keys to add:

| Key | Value |
|---|---|
| `HelpButtonText` | `?` |
| `HelpText` | `How to use` |
| `HelpTitleText` | `Desktop Todo Widget` |
| `HelpIntroText` | `This is a desktop to-do widget for PC.` |
| `HelpTasksHeadingText` | `TASKS` |
| `HelpTasksText` | the TASKS block below |
| `HelpWindowHeadingText` | `WINDOW` |
| `HelpWindowText` | the WINDOW block below |
| `HelpVisibilityHeadingText` | `IF YOU CANNOT SEE IT` |
| `HelpVisibilityText` | the visibility block below |

**R7 — exact wording.** Reproduce the text below character for character. Do not
paraphrase, reorder, renumber, or "improve" the English. `PC` is a person's
name, not an abbreviation — do not expand it and do not add an article.

```
This is a desktop to-do widget for PC.

TASKS
1. Type in the box at the bottom and press Enter to add a task.
2. Click the checkbox to complete a task — it stays where it is.
3. Double-click a task to edit it. Enter applies, Esc cancels.
4. Click × to delete a task.
5. Drag a task up or down to reorder it.
6. Click ⚙ to change that task's text size and colour.

WINDOW
1. Drag the bar at the top to move the widget.
2. Press Esc, or right-click and choose Exit, to close it.
3. Everything is saved automatically.

IF YOU CANNOT SEE IT
1. The widget stays behind other windows on purpose — minimise whatever is on top of it.
2. Win+D (show desktop) hides it too — press Win+D again.
3. Running the program again will not help; it is already running.
```

The last block is not filler. It is the measured conclusion recorded in
`findings.md` D2.1, and without it a user whose widget is covered will
double-click the program repeatedly and conclude it is broken.

**R8 — size.** The window is 360x420. The popup is a separate HWND and may
extend beyond the window bounds. If the content exceeds roughly 380px tall, wrap
it in a `ScrollViewer` using the existing `TodoVerticalScrollBarStyle`. Headings
should be distinguishable from body text using existing brushes
(`SecondaryTextBrush` for headings is consistent with the `⚙` popup).

**R9 — constraints.** No new NuGet packages. No network calls. No new files. No
changes outside the three listed above.

## Acceptance tests

Manual, per findings.md D6. UI behaviour is not unit tested in this project.

| # | Test | Expected |
|---|---|---|
| H1 | Look at the title bar | `?` at the right end, vertically centred, nothing clipped |
| H2 | Click `?` | Popup opens showing the exact text of R7 |
| H3 | Click elsewhere in the window | Popup closes |
| H4 | **Drag the title bar in an empty area** | **Window still moves** (regression) |
| H5 | Press and drag starting on `?` | Window does not move; a plain click still opens the popup |
| H6 | **With the popup open, press Esc** | **Popup closes, window stays open** |
| H7 | Popup closed, nothing being edited, press Esc | Window closes (unchanged) |
| H8 | Edit a task, open help, Esc, Esc, Esc | 1st closes popup (edit still active), 2nd cancels the edit, 3rd closes the window |
| H9 | Read the popup | All text visible, no clipping, readable on the dark background |
| H10 | Open a task's `⚙` popup | Still works (regression) |
| H11 | Reorder tasks by dragging | Still works (regression) |

H4, H6 and H10 are the ones that catch the predictable failures. Do not report
them as passing without actually performing them.

## Commands to run

**Do not attempt to build or test.** MSBuild cannot write to `obj/` inside your
sandbox (findings.md D7.1). Claude Code runs these outside the sandbox and will
send you the real output:

```
dotnet build src/DesktopTodoWidget/DesktopTodoWidget.csproj -c Release --no-restore
```

```
dotnet test tests/DesktopTodoWidget.Tests/DesktopTodoWidget.Tests.csproj --no-restore
```

Report the public signatures of anything you add or change, and state any
assumption you could not verify by compiling. Do not fabricate build or test
output.

## Risk notes

1. **The drag-strip swallow (R3).** This is the same code path that previously
   made the entire window undraggable, because `IsInsideControl` walked up into
   `Window`, which derives from `Control`. Verify H4 and H5 in both directions.
2. **Escape ordering (R4).** A wrong rung order closes the application while the
   help is open. H8 exercises the full stack.
3. **Popup focus (R5).** A `Popup` in its own HWND may swallow Escape entirely,
   producing a popup that cannot be dismissed by keyboard, or may let it through
   to the window and close the application. Neither is acceptable.
4. **Em dash and the `×` / `⚙` glyphs** appear in the help text. Ensure the file
   is saved as UTF-8 and the glyphs survive; they already render elsewhere in
   this UI, so no font change is needed.

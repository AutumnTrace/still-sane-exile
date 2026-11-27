# Accessibility

What SSE does for keyboard use, screen readers, contrast and text size, and what it doesn't do yet.

## Keyboard

- `Tab` and `Shift+Tab` move through every control in reading order: the header left to right, then the page. Lists of text, scroll areas and panels are never stops of their own.
- Every control shows when it has keyboard focus. Buttons, tick boxes, list rows and the slider get an outline in the text colour, text boxes a coloured border, and links in release notes a ring. The number boxes of an unticked stat are dimmed, but come up to full strength while one of them has focus.
- `Enter` or `Space` presses a button, `Space` ticks a box, and the arrow keys move through a list or change the slider.
- **Show prices in** in the header opens its menu with `Enter` or `Space`. Focus lands on the current currency: the arrow keys or `Tab` move, `Enter` picks one, `Esc` closes the menu and returns to the button.
- `Esc` closes the price check over the game (`F3`), and a quick price card you kept open, once you have clicked it. While you hold the key the card is click-through and never takes focus, so it has nothing to tab to. Kept open, `Tab` goes through its close button, its stats with their − and + buttons, and **All filters**.
- Keeping the card open means moving the mouse onto it while holding the key. **Press again to hide** (Settings → Quick price card) needs no held key: one press shows the card, the next hides it, and `F3` changes the search.
- Scroll bars page up or down when you click above or below the thumb.

## Screen readers

Every control has a name that says what it does and what it acts on, such as "Raise the minimum for 17% increased Attack Speed", "Remove the price 2.5 div", "Skip Defeat the Devourer" or "Change the quick price key". Rows of lists and tables read their contents ("Doom Thirst, Rare crossbow, checked at 13:30"), never the name of a program type. Buttons that pick one of a few (the page, the currency, the card position, how strict the search starts) report the chosen one with the item status "Selected".

This is checked with WPF's UI Automation peers and a UI Automation client, not yet with Narrator or NVDA in daily use. Reports are welcome.

## Contrast

Text meets WCAG AA (4.5:1) on every surface it appears on, including the selected rows, the header's current page, the ticked search rows and the quick price card and the guide's pinned lines over a bright game scene. The empty tick box meets the 3:1 minimum for controls. TextFaint and the tick box's grey were raised to reach these, and TextOff, the grey above TextFaint, with them, so the steps between the greys stay even:

| Colour | Before | After | On Surface (#101114) | Surface2 (#15161A) | Surface3 (#1B1D22) | Surface4 (#22242A) | Ticked row | Card over a white scene |
|---|---|---|---|---|---|---|---|---|
| TextOff (unticked stats) | `#8C929C` | `#959BA5` | 6.03 → 6.75 | 5.77 → 6.47 | 5.38 → 6.03 | 4.95 → 5.55 | 5.10 → 5.71 | 5.36 → 6.00 |
| TextFaint (hints, labels) | `#7E848F` | `#868C97` | 5.02 → 5.58 | 4.81 → 5.35 | **4.48** → 4.99 | **4.12** → 4.59 | **4.25** → 4.73 | **4.46** → 4.96 |
| Unchecked (empty tick box) | `#4A4E57` | `#666A73` | **2.26** → 3.48 | **2.17** → 3.34 | **2.02** → 3.11 | 1.86 → 2.86 | | **2.01** → 3.10 |

Bold values were below the minimum. A ticked row is the accent at 10 % over Surface2 (`#192231`); the card over a white scene is the card's 97 % Surface2 over white (`#1C1D21`). No tick box sits on Surface4.

The numbers of an unticked stat were drawn at 45 % opacity, which left them at 4.0 to 4.2:1 although they can still be changed; they are now at 55 % (5.4 to 5.7:1). Every other colour already passed and is unchanged: Text, TextSoft and TextDim (6.7:1 or more anywhere), the accent as text (4.7:1 on Surface4), links, warnings, hints, the rarity colours (4.9:1 or more on Surface4), the DEMO and DRAFT badges (11.8:1) and the dark text on the accent (5.7:1).

Disabled controls are dimmed to 45 % and are exempt from the contrast minimum, as WCAG allows: a disabled button reads at 4.0:1, the disabled **Apply** on My wealth at 2.1:1. The borders of buttons and boxes are quiet (1.3 to 1.4:1); their labels and the darker fill of the boxes identify them.

## Text size

WPF does not follow Windows' text size (Settings → Accessibility → Text size) by itself, because SSE sets its font sizes explicitly. SSE reads the setting (`HKCU\Software\Microsoft\Accessibility\TextScaleFactor`, 100 to 225 %) at start and whenever Windows reports a changed setting, and scales the main window and the price check over the game by it, like a browser's zoom. Display scaling (DPI) applies on top, as before.

Every page is laid out to work at the main window's minimum size (1260 × 640 at 100 %), so the minimum grows with the text size, and the factor stops where that minimum would no longer fit the screen. On a 1920 × 1080 screen at 100 % display scaling, text size works up to 152 %; at 125 % display scaling up to 121 %; at 150 % display scaling there is practically no room left (101 %). The price check over the game stops where it would no longer fit the game window, which on a 1080p game allows the full 225 %. At its largest factor a page looks as it does at the minimum window size: long lists scroll, and the longest names in the wealth tables end in an ellipsis with the full name in a tooltip.

The quick price card ignores the text size: it has its own size, Settings → Quick price card → Scale (0.75 to 1.6), so it can stay small over the game.

To check the layout without changing Windows' setting, render the screens at a text size: `--render-snapshots <dir> --data <empty dir> --text-scale 150`, optionally with `--window 1920x1032` for the size of the window (and the screen it must fit).

## Not done yet

- Windows contrast themes (high contrast) are not followed: SSE keeps its own dark colours.
- Changes such as a new price or "Copied" are not announced to screen readers as they happen; they are read when you move to them.

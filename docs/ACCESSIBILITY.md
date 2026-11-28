# Accessibility

What SSE does for keyboard use, screen readers, contrast and text size, and what it doesn't do yet.

## Keyboard

- `Tab` and `Shift+Tab` move through every control in reading order: the header left to right, then the page. Lists of text, scroll areas and panels are never stops of their own.
- Every control shows when it has keyboard focus. Buttons, tick boxes, list rows and the slider get a ring in the text colour, text boxes a gold ring, and links in release notes a ring. The number boxes of an unticked stat are dimmed, but come up to full strength while one of them has focus.
- `Enter` or `Space` presses a button, `Space` ticks a box, and the arrow keys move through a list or change the slider.
- **Show prices in** in the header opens its menu with `Enter` or `Space`. Focus lands on the current currency: the arrow keys or `Tab` move, `Enter` picks one, `Esc` closes the menu and returns to the button.
- `Esc` closes the price check over the game (`F3`). The quick price card never takes the keyboard from the game: while you hold the key it is click-through, and kept open it takes clicks only, so its close button, its stats with their − and + buttons, **Search** and **All filters** (in that order, if a window does give it focus) are used with the mouse. `F3` opens the same search in the price check, which works from the keyboard, **Search** included.
- Keeping the card open means moving the mouse onto it while holding the key. **Press again to hide** (Settings → Price card) needs no held key: one press shows the card, the next hides it, and `F3` changes the search.
- Settings is a rail of sections (League and prices, Keys, Price card, Price colours, Live prices and trade, Stash pictures, Updates and privacy) and one section at a time. In Price colours each colour is a row read in order: where it starts, then Divine or Exalted (the chosen one reports "Selected"); **Apply**, **Undo changes** and **Default steps** are only stops once there is something for them to do. The fine print under a setting is folded into **Details** links: `Space` or `Enter` opens and closes them, and screen readers hear them expand and collapse.
- Scroll bars page up or down when you click above or below the thumb.

## Screen readers

Every control has a name that says what it does and what it acts on, such as "Raise the minimum for 17% increased Attack Speed", "Remove the price 2.5 div", "Skip Defeat the Devourer" or "Change the quick price key". Rows of lists and tables read their contents ("Doom Thirst, Rare crossbow, checked at 13:30"), never the name of a program type. Buttons that pick one of a few (the page, the currency, the card position, how strict the search starts) report the chosen one with the item status "Selected".

This is checked with WPF's UI Automation peers and a UI Automation client, not yet with Narrator or NVDA in daily use. Reports are welcome.

## Contrast

Text meets WCAG AA (4.5:1) on every surface it appears on, including the selected rows, the header's current page, the ticked search rows and the quick price card over a bright game scene. The empty tick box meets the 3:1 minimum for controls. The look follows the game (dark bronze panels, gold for the search, stats in the game's blue, prices coloured like a loot filter); where the design's own colours fell short, they were raised until they passed:

| Colour | Design | In the app | Surface (#0E0D0B) | Surface2 (#15130F) | Surface3 (#1C1914) | Surface4 (#2A241B) | Ticked row | Card over a white scene |
|---|---|---|---|---|---|---|---|---|
| Text |  | same | 15.62 | 14.92 | 14.09 | 12.36 | 12.07 | 14.81 |
| TextSoft |  | same | 11.45 | 10.94 | 10.33 | 9.06 | 8.85 | 10.85 |
| TextDim |  | same | 7.80 | 7.45 | 7.03 | 6.17 | 6.03 | 7.39 |
| TextOff (unticked properties) |  |  | 6.97 | 6.66 | 6.29 | 5.51 | 5.39 | 6.61 |
| TextFaint (hints, labels) |  (**4.05** on Surface4, **3.96** on a ticked row) |  | 5.94 | 5.67 | 5.36 | 4.70 | 4.59 | 5.63 |
| Stat, searched |  | same | 8.98 | 8.58 | 8.10 | 7.11 | 6.94 | 8.51 |
| Stat, not searched |  (**3.97** on Surface4) |  | 5.91 | 5.64 | 5.33 | 4.67 | 4.56 | 5.60 |
| Gold (accent) as text |  | same | 8.58 | 8.20 | 7.74 | 6.79 | 6.63 | 8.14 |
| Unchecked (empty tick box) |  (**2.23** on Surface2) |  | 3.70 | 3.54 | 3.34 | 2.93 | 2.86 | 3.51 |

Bold values were below the minimum. A ticked row is the gold at 13 % over Surface2 (); the card over a white scene is the card's 97 % Surface over white. No empty tick box sits on Surface4 or a ticked row. Links (, 8.2:1 or more), warnings and hints (8.2:1 or more), the rarity colours on item names (4.9:1 or more on Surface4), the gold on the chosen button (12.7:1) and the dark tick on gold (8.0:1) pass everywhere.

Price labels are solid tags in loot-filter colours. Their text reads at: white and red 5.9:1 (10 Divine or more), red 5.9:1 (1 to 10 Divine), orange 4.9:1, amber 9.1:1, tan 10.6:1 and dark 10.2:1 (under 1 Exalted). The design's orange label had white text at 3.7:1; it takes dark text instead.

The numbers of an unticked stat are drawn at 55 % opacity, about 5.2:1, because they can still be changed; they come up to full strength while one of them has focus. A price and listings whose search you have changed since are faded to 65 % until you press **Search**, and the line under the price says *Search to update* at full strength. Disabled controls are dimmed to 45 % and are exempt from the contrast minimum, as WCAG allows. The borders of buttons and boxes are quiet (1.4 to 2.7:1); their labels and the darker fill of the boxes identify them.

Keyboard focus is a 2 px ring in the text colour over a button's or a box's border, and around a tick box: a change of border colour alone was too faint on gold.

The roll bar under a stat is drawn with the empty tick box's colour for its rail (3:1 or more on every surface the card uses), the bar colour for its fill and the text colour for the marker, which stands taller than the rail so it reads on the fill too. Its lowest and highest numbers are in TextFaint. Screen readers hear the line as words, "Tier 8, rolls 170 to 179", and a stat's row reads its text, then that line.

## Text size

WPF does not follow Windows' text size (Settings → Accessibility → Text size) by itself, because SSE sets its font sizes explicitly. SSE reads the setting (`HKCU\Software\Microsoft\Accessibility\TextScaleFactor`, 100 to 225 %) at start and whenever Windows reports a changed setting, and scales the main window and the price check over the game by it, like a browser's zoom. Display scaling (DPI) applies on top, as before.

Every page is laid out to work at the main window's minimum size (1260 × 640 at 100 %), so the minimum grows with the text size, and the factor stops where that minimum would no longer fit the screen. On a 1920 × 1080 screen at 100 % display scaling, text size works up to 152 %; at 125 % display scaling up to 121 %; at 150 % display scaling there is practically no room left (101 %). The price check over the game stops where it would no longer fit the game window, which on a 1080p game allows the full 225 %. At its largest factor a page looks as it does at the minimum window size: long lists scroll, and the longest names in the wealth tables end in an ellipsis with the full name in a tooltip.

The quick price card ignores the text size: it has its own size, Settings → Price card → Scale (0.5 to 1.6, 0.85 by default), so it can stay small over the game.

To check the layout without changing Windows' setting, render the screens at a text size: `--render-snapshots <dir> --data <empty dir> --text-scale 150`, optionally with `--window 1920x1032` for the size of the window (and the screen it must fit).

## Not done yet

- Windows contrast themes (high contrast) are not followed: SSE keeps its own dark colours.
- Changes such as a new price or "Copied" are not announced to screen readers as they happen; they are read when you move to them.

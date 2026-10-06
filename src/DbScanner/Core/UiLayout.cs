namespace DbScanner.Core
{
    /// <summary>
    /// Positions in the game's 1152×768 layout space, read from the client's own UI symbols
    /// (a_ArmoryWindow and a_ScreenHudTooltip in UI_4.swf) and from ScreenArmory, which shows
    /// every gear tooltip at (370.5, 387) and every charm tooltip at (369.25, 364.05).
    /// </summary>
    public static class UiLayout
    {
        public const int GridColumns = 7;
        public const int GridRows = 4;
        public const int SlotsPerPage = 28;

        static readonly string[] SlotKeys = { "mainhand", "offhand", "hat", "armor", "gloves", "boots" };
        public static string[] EquipmentSlots { get { return (string[])SlotKeys.Clone(); } }

        /// <summary>Inventory grid slot (0-27, left to right, top to bottom).</summary>
        public static RectD GridSlot(int i)
        {
            int c = i % GridColumns, r = i / GridColumns;
            double x = 761.9 + 51 * c, y = 433.1 + 51 * r;
            return new RectD(x, y, x + 54.2, y + 54.4);
        }

        /// <summary>The stack size printed on a charm or material slot.</summary>
        public static RectD GridQuantity(int i)
        {
            RectD s = GridSlot(i);
            return new RectD(s.X0 + 4.2, s.Y0 + 32.7, s.X0 + 49.5, s.Y0 + 52.0);
        }

        public static readonly RectD PageLeft = new RectD(1035.7, 396.6, 1056.7, 416.1);
        public static readonly RectD PageRight = new RectD(1062.1, 396.6, 1083.1, 416.1);
        public static readonly RectD HeaderText = new RectD(757.0, 393.9, 1030.0, 419.1);
        public static readonly RectD CharacterName = new RectD(759.0, 27.9, 1085.0, 53.1);
        public static readonly RectD InventoryPanel = new RectD(731.9, 6.6, 1150.8, 664.4);

        public static readonly RectD TabGear = new RectD(697.2, 390.2, 733.5, 430.0);
        public static readonly RectD TabCharms = new RectD(702.1, 429.5, 733.5, 469.4);

        /// <summary>Equipped gear on the paper doll, main hand to boots.</summary>
        public static RectD EquipmentSlot(int k)
        {
            double y = 70.8 + 51 * k;
            return new RectD(761.4, y, 813.7, y + 52.7);
        }

        /// <summary>Charm socket j (0-2) of equipped piece k (0-5), shown on the Charms tab.</summary>
        public static RectD CharmSocket(int k, int j)
        {
            double x = 853.1 + 46 * j, y = 74.2 + 51 * k + (k == 5 ? 0.2 : 0);
            return new RectD(x, y, x + 48.5, y + 48.5);
        }

        /* ---------- gear tooltip (am_ItemDetail shown at 370.5, 387) ---------- */

        public static readonly RectD GearTip = new RectD(370.5, 388.0, 696.5, 618.0);
        public static readonly RectD GearTipName = new RectD(378.7, 394.2, 690.6, 419.5);
        public static readonly RectD[] GearTipStats =
        {
            new RectD(480.7, 424.1, 640.0, 445.7),
            new RectD(480.7, 446.1, 640.0, 467.7),
            new RectD(480.7, 468.1, 640.0, 491.0)
        };
        public static readonly RectD[] GearTipMagic =
        {
            new RectD(480.7, 490.1, 686.5, 513.0),
            new RectD(480.7, 512.0, 686.5, 534.8)
        };
        public static readonly RectD[] GearTipGems =
        {
            new RectD(482.3, 536.0, 525.5, 579.2),
            new RectD(523.7, 536.0, 566.8, 579.2),
            new RectD(564.7, 536.0, 607.9, 579.2)
        };
        public static readonly RectD GearTipType = new RectD(380.5, 556.1, 474.5, 577.7);
        public static readonly RectD GearTipPowerRune = new RectD(407.7, 584.6, 682.6, 606.2);
        public static readonly RectD[] GearTipProcRunes =
        {
            new RectD(407.7, 610.0, 682.6, 632.8),
            new RectD(407.7, 636.0, 681.6, 658.8)
        };

        /// <summary>Points inside the gear tooltip's dark box that text never covers.</summary>
        public static PointD[] GearTipBackground()
        {
            // Right of the stat lines and right of the charm gems: plain fill on every gear tooltip.
            return new[]
            {
                new PointD(660, 430), new PointD(660, 445), new PointD(660, 460), new PointD(660, 475),
                new PointD(640, 545), new PointD(660, 557), new PointD(675, 570)
            };
        }

        /// <summary>The middle of a grid slot, where the item's icon is drawn.</summary>
        public static RectD GridIcon(int i)
        {
            RectD s = GridSlot(i);
            return new RectD(s.X0 + 8, s.Y0 + 8, s.X0 + 46, s.Y0 + 46);
        }

        public static readonly RectD Grid = new RectD(761.9, 433.1, 1123.2, 640.5);

        /// <summary>The blue "E" badge drawn on equipped gear in the bag grid.</summary>
        public static RectD GridEquippedBadge(int i)
        {
            RectD s = GridSlot(i);
            return new RectD(s.X0 + 0.5, s.Y0 + 26.7, s.X0 + 26.3, s.Y0 + 52.5);
        }

        /* ---------- charm tooltip (am_CharmDetail shown at 369.25, 364.05) ---------- */

        public static readonly RectD CharmTip = new RectD(368.95, 387.05, 690.95, 475.05);
        public static readonly RectD CharmTipName = new RectD(380.45, 396.25, 684.25, 421.55);
        public static readonly RectD CharmTipType = new RectD(380.45, 418.15, 538.25, 441.05);
        public static readonly RectD[] CharmTipStats =
        {
            new RectD(404.35, 441.55, 573.35, 463.15),
            new RectD(404.35, 466.55, 573.35, 488.15),
            new RectD(404.35, 491.55, 573.35, 513.15)
        };

        public static PointD[] CharmTipBackground()
        {
            return new[]
            {
                new PointD(650, 400), new PointD(660, 412), new PointD(660, 426), new PointD(665, 440), new PointD(670, 455), new PointD(670, 466)
            };
        }

        /* ---------- Tome of Power (a_ScreenTome in UI_4.swf) ---------- */

        public const int TomeTabs = 5;
        public const int TomeColumns = 4;
        public const int TomeSlots = 12;

        /// <summary>The page tabs down the book's left edge: the class page, the three disciplines, and the master page at the bottom.</summary>
        public static RectD TomeTab(int i)
        {
            double y = i < 4 ? 111.8 + 66.03 * i : 436.9;
            return new RectD(91.0, y, 160.7, y + 68.0);
        }

        /// <summary>Ability slot i of the open page: four columns, one row per tier.</summary>
        public static RectD TomeSlot(int i)
        {
            int c = i % TomeColumns, r = i / TomeColumns;
            double x = 207.7 + 77.0 * c;
            double y = r == 0 ? 175.0 : r == 1 ? 305.0 : 445.0;
            return new RectD(x, y, x + 76.5, y + 76.6);
        }

        /// <summary>The middle of a slot, where the ability's icon is drawn.</summary>
        public static RectD TomeSlotIcon(int i)
        {
            RectD s = TomeSlot(i);
            return new RectD(s.X0 + 14, s.Y0 + 14, s.X1 - 14, s.Y1 - 22);
        }

        public static readonly RectD TomeExit = new RectD(991.6, 91.4, 1023.3, 121.7);
        /// <summary>Plain parchment under the third tier: somewhere to rest the mouse that shows no tooltip.</summary>
        public static readonly PointD TomePark = new PointD(380, 585);

        /* ---------- the hotbar (a_Hud, am_PowerButtons) ---------- */

        /// <summary>The six ability buttons, keys 1, 2, 3, 4, E and Q by default.</summary>
        public static RectD HotbarKey(int i)
        {
            double x = 100.0 + 54.5 * i;
            return new RectD(x, 702.9, x + 56.3, 759.2);
        }

        public static readonly string[] HotbarLabels = { "1", "2", "3", "4", "E", "Q" };

        /* ---------- ability tooltip (am_HudPowerDetails: the Tome and the hotbar show the same one) ---------- */

        public static readonly RectD AbilityTip = new RectD(683.4, 635.4, 1119.3, 759.4);
        public static readonly RectD AbilityTipName = new RectD(694.6, 645.6, 1109.0, 670.9);
        /// <summary>"Rank 10" on the left, "Mana Cost: 20" on the right.</summary>
        public static readonly RectD AbilityTipType = new RectD(694.6, 667.5, 1105.6, 690.4);
        /// <summary>The description's three visible lines; the box cuts off anything longer.</summary>
        public static readonly RectD[] AbilityTipDesc =
        {
            new RectD(694.6, 690.6, 1105.0, 709.4),
            new RectD(694.6, 709.0, 1105.0, 727.0),
            new RectD(694.6, 726.6, 1105.0, 745.0)
        };

        /// <summary>Points inside the ability tooltip's frame that its text never reaches.</summary>
        public static PointD[] AbilityTipBackground()
        {
            return new[]
            {
                new PointD(1110, 652), new PointD(1110, 700), new PointD(1110, 720), new PointD(1110, 740),
                new PointD(690, 655), new PointD(690, 700), new PointD(690, 738)
            };
        }

        /// <summary>The tooltips' dark fill, (38, 32, 1).</summary>
        public static readonly int[] TipFill = { 38, 32, 1 };

        /* ---------- text colours ---------- */

        public static readonly int[] Parchment = { 238, 226, 188 };   // normal text, Magic item names
        public static readonly int[] Cyan = { 0, 204, 255 };          // rune and find-rune lines
        public static readonly int[] Legendary = { 248, 221, 69 };    // 0xF8DD45
        public static readonly int[] Rare = { 0, 153, 255 };          // 0x0099FF
    }
}

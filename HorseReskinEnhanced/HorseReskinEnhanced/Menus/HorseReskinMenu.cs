using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using HorseReskinEnhanced.Messages;
using Microsoft.Xna.Framework.Input;

namespace HorseReskinEnhanced.Menus
{
    public class HorseReskinMenu : IClickableMenu
    {
        private int CurrentSkinId = 1;
        private readonly Dictionary<int, Lazy<Texture2D>> SkinTextureMap;
        private readonly Dictionary<int, string> SkinNameMap;
        private readonly Guid HorseId;
        private ClickableTextureComponent HorsePreview;
        private ClickableTextureComponent BackButton;
        private ClickableTextureComponent ForwardButton;
        private ClickableTextureComponent OkButton;
        private ClickableTextureComponent CloseButton;
        private int animationFrame = 0;
        private float animationTimer = 0f;
        private const float AnimationFrameDuration = 150f; // ms per frame

        private const int HorseSpriteWidth = 32;
        private const int HorseSpriteHeight = 32;
        private const float HorsePreviewScale = 5f;
        private const int HorseSpriteIndexBase = 8; // Start of walking animation
        private const int HorseSpriteFrameCount = 4; // Frames 8-11
        private const int MenuPadding = 80;
        private const int ButtonSpacing = 36;
        private const int OkButtonWidth = 64;
        private const int OkButtonHeight = 64;
        private const int BackButtonWidth = 48;
        private const int ForwardButtonWidth = 48;
        private const int BackButtonHeight = 44;
        private const int ForwardButtonHeight = 44;
        private const int CloseButtonWidth = 42;
        private const int CloseButtonHeight = 42;
        private const int TitlePadding = 12;
        private const int ButtonMargin = 40; // Forward button margin
        private const int BackButtonMargin = 20; // Left arrow shifted left
        private const int CloseButtonInset = 8;
        private const int TitleBoxHeight = 64;
        private const int TitleTextPadding = 40;
        private const int SkinNamePadding = 10;

        private static readonly int MaxWidthOfMenu = 400;
        private static readonly int MaxHeightOfMenu = 256;
        private static readonly Rectangle MenuBackgroundRect = new Rectangle(0, 320, 60, 60); // Parchment background

        private readonly int BackButtonId = 44;
        private readonly int ForwardButtonId = 33;
        private readonly int OkButtonId = 46;
        private readonly int CloseButtonId = 47;

        public HorseReskinMenu(Guid horseId, Dictionary<int, Lazy<Texture2D>> skinTextureMap, Dictionary<int, string> skinNameMap) : base(0, 0, 0, 0, true)
        {
            HorseId = horseId;
            SkinTextureMap = new Dictionary<int, Lazy<Texture2D>>(skinTextureMap);
            SkinNameMap = new Dictionary<int, string>(skinNameMap);
            if (SkinTextureMap.Count == 0)
            {
                ModEntry.SMonitor.Log("No textures available for HorseReskinMenu.", LogLevel.Error);
                Game1.addHUDMessage(new HUDMessage("No horse skins available.", HUDMessage.error_type));
                return;
            }

            BackButton = new ClickableTextureComponent(Rectangle.Empty, null, Rectangle.Empty, 0f);
            ForwardButton = new ClickableTextureComponent(Rectangle.Empty, null, Rectangle.Empty, 0f);
            OkButton = new ClickableTextureComponent(Rectangle.Empty, null, Rectangle.Empty, 0f);
            CloseButton = new ClickableTextureComponent(Rectangle.Empty, null, Rectangle.Empty, 0f);
            ResetBounds();
            Game1.playSound("bigSelect");
        }

        private Texture2D CurrentHorseTexture => SkinTextureMap[CurrentSkinId].Value;

        public override void receiveGamePadButton(Buttons b)
        {
            base.receiveGamePadButton(b);
            switch (b)
            {
                case Buttons.LeftTrigger: CycleSkin(-1, BackButton); break;
                case Buttons.RightTrigger: CycleSkin(1, ForwardButton); break;
                case Buttons.A: SelectSkin(); exitThisMenu(); Game1.playSound("smallSelect"); break;
                case Buttons.B: exitThisMenu(); Game1.playSound("bigDeSelect"); break;
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);
            if (BackButton.containsPoint(x, y)) CycleSkin(-1, BackButton);
            else if (ForwardButton.containsPoint(x, y)) CycleSkin(1, ForwardButton);
            else if (OkButton.containsPoint(x, y)) { SelectSkin(); exitThisMenu(); Game1.playSound("smallSelect"); }
            else if (CloseButton.containsPoint(x, y)) { exitThisMenu(); Game1.playSound("bigDeSelect"); }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            BackButton?.tryHover(x, y, 0.2f);
            ForwardButton?.tryHover(x, y, 0.2f);
            OkButton?.tryHover(x, y, 0.2f);
            CloseButton?.tryHover(x, y, 0.2f);
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => ResetBounds();

        public override void update(GameTime time)
        {
            base.update(time);
            animationTimer += (float)time.ElapsedGameTime.TotalMilliseconds;
            if (animationTimer >= AnimationFrameDuration)
            {
                animationFrame = (animationFrame + 1) % HorseSpriteFrameCount;
                animationTimer = 0f;
                UpdateHorsePreview();
            }
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            string title = ModEntry.SHelper.Translation.Get("menu.title", new { defaultValue = "Horse Reskin" });
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title);
            int titleBoxWidth = MaxWidthOfMenu;
            int titleBoxX = xPositionOnScreen + (width - titleBoxWidth) / 2;
            int titleBoxY = yPositionOnScreen - TitleBoxHeight - TitlePadding;
            IClickableMenu.drawTextureBox(b, titleBoxX, titleBoxY, titleBoxWidth, TitleBoxHeight, Color.White);
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(titleBoxX + (titleBoxWidth - titleSize.X) / 2, titleBoxY + (TitleBoxHeight - titleSize.Y) / 2), Game1.textColor);

            b.Draw(Game1.mouseCursors, new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height), MenuBackgroundRect, Color.White * 0.5f);
            IClickableMenu.drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

            HorsePreview?.draw(b);
            BackButton?.draw(b, BackButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.PaleGoldenrod : Color.White, 0.9f);
            ForwardButton?.draw(b, ForwardButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.PaleGoldenrod : Color.White, 0.9f);
            OkButton?.draw(b, OkButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.PaleGoldenrod : Color.White, 0.9f);
            CloseButton?.draw(b, CloseButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.PaleGoldenrod : Color.White, 0.9f);

            if (SkinNameMap.TryGetValue(CurrentSkinId, out var skinName))
            {
                Vector2 skinNameSize = Game1.smallFont.MeasureString(skinName);
                Utility.drawTextWithShadow(b, skinName, Game1.smallFont, new Vector2(xPositionOnScreen + (width - skinNameSize.X) / 2, OkButton.bounds.Y + OkButtonHeight + SkinNamePadding), Game1.textColor);
            }

            if (SkinTextureMap.Count == 0)
            {
                string message = "No horse skins available.";
                Vector2 size = Game1.dialogueFont.MeasureString(message);
                Vector2 pos = new(xPositionOnScreen + (width - size.X) / 2, yPositionOnScreen + (height - size.Y) / 2);
                b.DrawString(Game1.dialogueFont, message, pos, Color.Red);
            }
            drawMouse(b);
        }

        private void CycleSkin(int direction, ClickableTextureComponent button)
        {
            CurrentSkinId = (CurrentSkinId - 1 + direction + SkinTextureMap.Count) % SkinTextureMap.Count + 1;
            Game1.playSound("shwip");
            button.scale = button.baseScale;
            animationFrame = 0;
            UpdateHorsePreview();
        }

        private void SelectSkin()
        {
            if (!ModEntry.HorseNameMap.TryGetValue(HorseId, out var horse) || horse == null)
            {
                ModEntry.SMonitor.Log($"No horse found for ID {HorseId}", LogLevel.Error);
                Game1.addHUDMessage(new HUDMessage("Invalid horse.", HUDMessage.error_type));
                return;
            }

            if (Context.IsMainPlayer)
                ModEntry.SaveHorseReskin(HorseId, CurrentSkinId);
            else
                ModEntry.SHelper.Multiplayer.SendMessage(new HorseReskinMessage(HorseId, CurrentSkinId), ModEntry.ReskinHorseMessageId, new[] { ModEntry.SModManifest.UniqueID });
        }

        private void UpdateHorsePreview()
        {
            int previewWidth = (int)(HorseSpriteWidth * HorsePreviewScale);
            int previewHeight = (int)(HorseSpriteHeight * HorsePreviewScale);
            int previewX = xPositionOnScreen + (width - previewWidth) / 2;
            int previewY = yPositionOnScreen + (height - previewHeight - OkButtonHeight - SkinNamePadding - ButtonSpacing) / 2 + 6;
            HorsePreview = new ClickableTextureComponent(
                new Rectangle(previewX, previewY, previewWidth, previewHeight),
                CurrentHorseTexture,
                Game1.getSourceRectForStandardTileSheet(CurrentHorseTexture, HorseSpriteIndexBase + animationFrame, HorseSpriteWidth, HorseSpriteHeight),
                HorsePreviewScale
            );
        }

        private void ResetBounds()
        {
            xPositionOnScreen = Game1.uiViewport.Width / 2 - MaxWidthOfMenu / 2 - spaceToClearSideBorder;
            yPositionOnScreen = Game1.uiViewport.Height / 2 - MaxHeightOfMenu / 2 - spaceToClearTopBorder;
            width = MaxWidthOfMenu + spaceToClearSideBorder * 2;
            height = MaxHeightOfMenu + spaceToClearTopBorder;
            initialize(xPositionOnScreen, yPositionOnScreen, width, height, true);

            int buttonY = yPositionOnScreen + height - OkButtonHeight - SkinNamePadding - ButtonSpacing;

            BackButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + BackButtonMargin, buttonY, BackButtonWidth, BackButtonHeight),
                Game1.mouseCursors,
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, BackButtonId),
                1f
            )
            { myID = BackButtonId, rightNeighborID = OkButtonId };

            OkButton = new ClickableTextureComponent(
                "OK",
                new Rectangle(xPositionOnScreen + (width - OkButtonWidth) / 2, buttonY, OkButtonWidth, OkButtonHeight),
                null,
                "Apply Skin",
                Game1.mouseCursors,
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, OkButtonId),
                1f
            )
            { myID = OkButtonId, leftNeighborID = BackButtonId, rightNeighborID = ForwardButtonId };

            ForwardButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - ForwardButtonWidth - ButtonMargin, buttonY, ForwardButtonWidth, ForwardButtonHeight),
                Game1.mouseCursors,
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, ForwardButtonId),
                1f
            )
            { myID = ForwardButtonId, leftNeighborID = OkButtonId };

            int closeButtonY = yPositionOnScreen + CloseButtonInset;
            CloseButton = new ClickableTextureComponent(
                "Close",
                new Rectangle(xPositionOnScreen + width - CloseButtonWidth - CloseButtonInset, closeButtonY, CloseButtonWidth, CloseButtonHeight),
                null,
                "Close Menu",
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                3.5f
            )
            { myID = CloseButtonId };

            allClickableComponents = new List<ClickableComponent> { BackButton, OkButton, ForwardButton, CloseButton };

            if (Game1.options.gamepadControls)
            {
                currentlySnappedComponent = OkButton;
                snapCursorToCurrentSnappedComponent();
            }
        }
    }
}
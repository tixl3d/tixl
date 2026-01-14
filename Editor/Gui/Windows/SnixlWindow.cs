using ImGuiNET;
using System.Numerics;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// An easter egg window implementing a simple Snake game (SNiXL) using ImGui for UI rendering.
/// The game features a grid, snake movement, food spawning, score tracking, and game states (menu, playing, game over).
/// An example of how to extend TiXL with windows and interactive content.
/// </summary>
internal sealed class SnixlWindow : Window
{
    /// <summary>
    /// Initializes the SNiXL window with title and window flags. Hidden by default.
    /// </summary>
    internal SnixlWindow()
    {
        Config.Title = "SNiXL";
        Config.Visible = false;
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize;
    }

    /// <summary>
    /// Possible movement directions for the snake.
    /// </summary>
    private enum Direction { Up, Down, Left, Right }
    /// <summary>
    /// Game states: Menu (start), Playing, GameOver.
    /// </summary>
    private enum GameState { Menu, Playing, GameOver }

    /// <summary>
    /// Main draw loop for the SNiXL window. Handles game logic, input, and rendering.
    /// </summary>
    protected override void DrawContent()
    {
        const int gridSize = 20; // Number of cells per row/column
        const float updateInterval = 0.15f; // Snake movement interval (seconds)
        const float cellPadding = 2f; // Padding inside each cell for visuals

        ImGui.BeginChild("##snakeContent", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar);

        // Calculate grid and cell sizes based on available window space
        var availableSpace = ImGui.GetContentRegionAvail();
        var textAreaHeight = ImGui.GetTextLineHeight() * 2.7f;
        var cellSize = MathF.Min((availableSpace.X - 32f) / gridSize, (availableSpace.Y - textAreaHeight - 2f) / gridSize);
        var gridPixelSize = gridSize * cellSize;
        var gridOrigin = ImGui.GetCursorScreenPos() + new Vector2((availableSpace.X - gridPixelSize) * 0.5f, 2f);

        // Initialize game if not started
        if (_snake.Count == 0) InitializeGame(gridSize);

        // Update timer for snake movement
        _timeSinceLastUpdate += ImGui.GetIO().DeltaTime;

        // Handle keyboard input if window is hovered/focused
        if (ImGui.IsWindowHovered() || ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows))
        {
            // Start or restart game on ENTER
            if (_gameState != GameState.Playing && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)))
            {
                InitializeGame(gridSize);
                _gameState = GameState.Playing;
            }
            
            // Handle direction input and update game logic
            if (_gameState == GameState.Playing)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, false) && _currentDirection != Direction.Down) _nextDirection = Direction.Up;
                else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, false) && _currentDirection != Direction.Up) _nextDirection = Direction.Down;
                else if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false) && _currentDirection != Direction.Right) _nextDirection = Direction.Left;
                else if (ImGui.IsKeyPressed(ImGuiKey.RightArrow, false) && _currentDirection != Direction.Left) _nextDirection = Direction.Right;

                // Move snake at fixed interval
                if (_timeSinceLastUpdate >= updateInterval)
                {
                    _timeSinceLastUpdate = 0;
                    _currentDirection = _nextDirection;
                    UpdateGame(gridSize);
                }
            }
        }

        var drawList = ImGui.GetWindowDrawList();

        // Draw grid background
        drawList.AddRectFilled(gridOrigin, gridOrigin + new Vector2(gridPixelSize, gridPixelSize), UiColors.BackgroundFull.Fade(0.8f));

        // Draw grid lines
        for (int i = 0; i <= gridSize; i++)
        {
            var offset = i * cellSize;
            drawList.AddLine(gridOrigin + new Vector2(offset, 0), gridOrigin + new Vector2(offset, gridPixelSize), UiColors.BackgroundButton.Fade(0.3f));
            drawList.AddLine(gridOrigin + new Vector2(0, offset), gridOrigin + new Vector2(gridPixelSize, offset), UiColors.BackgroundButton.Fade(0.3f));
        }

        // Draw snake and food if not in menu
        if (_gameState != GameState.Menu)
        {
            // Draw food
            drawList.AddRectFilled(
                gridOrigin + new Vector2(_foodX * cellSize + cellPadding, _foodY * cellSize + cellPadding),
                gridOrigin + new Vector2((_foodX + 1) * cellSize - cellPadding, (_foodY + 1) * cellSize - cellPadding),
                UiColors.StatusError);

            // Draw snake segments
            for (int i = 0; i < _snake.Count; i++)
            {
                var (x, y) = _snake[i];
                var color = _gameState == GameState.GameOver ? UiColors.StatusError.Fade(0.7f) : (i == 0 ? UiColors.StatusAutomated : UiColors.StatusAnimated);
                drawList.AddRectFilled(
                    gridOrigin + new Vector2(x * cellSize + cellPadding, y * cellSize + cellPadding),
                    gridOrigin + new Vector2((x + 1) * cellSize - cellPadding, (y + 1) * cellSize - cellPadding),
                    color);
            }
        }

        // Draw overlays for menu/game over
        if (_gameState != GameState.Playing)
        {
            drawList.AddRectFilled(gridOrigin, gridOrigin + new Vector2(gridPixelSize, gridPixelSize), 
                UiColors.BackgroundFull.Fade(_gameState == GameState.Menu ? 0.85f : 0.7f));
            
            ImGui.PushFont(Fonts.FontLarge);
            DrawCenteredText(drawList, "SNiXL", gridOrigin, gridPixelSize, 0.3f, UiColors.StatusAnimated, UiColors.BackgroundFull);
            ImGui.PopFont();
            
            if (_gameState == GameState.Menu)
            {
                DrawCenteredText(drawList, "Press ENTER to start", gridOrigin, gridPixelSize, 0.5f, UiColors.Text, UiColors.BackgroundFull);
                if (_highScore > 0)
                    DrawCenteredText(drawList, $"High Score: {_highScore}", gridOrigin, gridPixelSize, 0.65f, UiColors.TextMuted);
            }
            else // GameOver
            {
                // GAME OVER text is intentionally moved down to avoid overlap with SNiXL title
                DrawCenteredText(drawList, "GAME OVER", gridOrigin, gridPixelSize, 0.42f, UiColors.StatusError, UiColors.BackgroundFull);
                DrawCenteredText(drawList, $"Score: {_score}", gridOrigin, gridPixelSize, 0.48f, UiColors.Text);
                DrawCenteredText(drawList, "Press ENTER to play again", gridOrigin, gridPixelSize, 0.6f, UiColors.Text, UiColors.BackgroundFull);
            }
        }

        ImGui.Dummy(new Vector2(0, gridPixelSize + 2f));
        ImGui.Spacing();

        // Draw help and score text below the grid
        if (_gameState == GameState.Playing)
        {
            DrawTextWithInlineFood(drawList, $"Score: {_score}    High Score: {_highScore}", gridOrigin, gridPixelSize);
            ImGui.Spacing();
            DrawTextWithInlineFood(drawList, "Use arrow keys to control the snake. Eat the|food to grow!", gridOrigin, gridPixelSize);
        }
        else if (_gameState == GameState.GameOver)
        {
            DrawTextWithInlineFood(drawList, "GAME OVER!", gridOrigin, gridPixelSize);
            DrawTextWithInlineFood(drawList, $"Final Score: {_score}    High Score: {_highScore}", gridOrigin, gridPixelSize);
        }
        else // Menu
        {
            DrawTextWithInlineFood(drawList, "Control the snake with arrow keys and eat the|food to grow.", gridOrigin, gridPixelSize);
            DrawTextWithInlineFood(drawList, "Don't hit the walls or yourself!", gridOrigin, gridPixelSize);
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// SNiXL is a singleton window (no instances).
    /// </summary>
    internal override List<Window> GetInstances() => new();

    /// <summary>
    /// Initializes or resets the game state, snake, and food.
    /// </summary>
    private void InitializeGame(int gridSize)
    {
        _snake.Clear();
        _snake.Add((gridSize / 2, gridSize / 2));
        _currentDirection = _nextDirection = Direction.Right;
        _score = 0;
        _timeSinceLastUpdate = 0;
        SpawnFood(gridSize);
    }

    /// <summary>
    /// Updates the snake's position, handles collisions, and manages score/food.
    /// </summary>
    private void UpdateGame(int gridSize)
    {
        var (headX, headY) = _snake[0];
        var (newX, newY) = _currentDirection switch
        {
            Direction.Up => (headX, headY - 1),
            Direction.Down => (headX, headY + 1),
            Direction.Left => (headX - 1, headY),
            Direction.Right => (headX + 1, headY),
            _ => (headX, headY)
        };

        // Check for wall or self collision
        if (newX < 0 || newX >= gridSize || newY < 0 || newY >= gridSize || _snake.Any(s => s.X == newX && s.Y == newY))
        {
            _gameState = GameState.GameOver;
            return;
        }

        // Move snake head
        _snake.Insert(0, (newX, newY));

        // Check for food collision
        if (newX == _foodX && newY == _foodY)
        {
            if (++_score > _highScore) _highScore = _score;
            SpawnFood(gridSize);
        }
        else
        {
            // Remove tail if no food eaten
            _snake.RemoveAt(_snake.Count - 1);
        }
    }

    /// <summary>
    /// Randomly spawns food on the grid, avoiding the snake's body.
    /// </summary>
    private void SpawnFood(int gridSize)
    {
        var random = new Random();
        do
        {
            (_foodX, _foodY) = (random.Next(0, gridSize), random.Next(0, gridSize));
        }
        while (_snake.Any(s => s.X == _foodX && s.Y == _foodY));
    }

    /// <summary>
    /// Draws centered text on the grid, with optional shadow.
    /// </summary>
    private void DrawCenteredText(ImDrawListPtr drawList, string text, Vector2 gridOrigin, float gridPixelSize, float yOffsetRatio, uint color, uint? shadowColor = null)
    {
        var textSize = ImGui.CalcTextSize(text);
        var textPos = gridOrigin + new Vector2((gridPixelSize - textSize.X) * 0.5f, gridPixelSize * yOffsetRatio);
        if (shadowColor.HasValue)
            drawList.AddText(textPos + Vector2.One * (shadowColor == UiColors.BackgroundFull ? 3 : 1), shadowColor.Value, text);
        drawList.AddText(textPos, color, text);
    }

    /// <summary>
    /// Draws a line of text centered below the grid, optionally with an inline food icon (use '|' as separator).
    /// </summary>
    private void DrawTextWithInlineFood(ImDrawListPtr drawList, string text, Vector2 gridOrigin, float gridPixelSize)
    {
        var parts = text.Split('|');
        if (parts.Length == 1)
        {
            var size = ImGui.CalcTextSize(text);
            var pos = new Vector2(gridOrigin.X + (gridPixelSize - size.X) * 0.5f, ImGui.GetCursorScreenPos().Y);
            ImGui.SetCursorScreenPos(pos);
            ImGui.TextUnformatted(text);
            return;
        }

        // Draws text with a colored food rectangle inline
        var beforeSize = ImGui.CalcTextSize(parts[0] + " ");
        var afterSize = ImGui.CalcTextSize(" " + parts[1]);
        var totalWidth = beforeSize.X + 14 + afterSize.X;
        var y = ImGui.GetCursorScreenPos().Y;
        var x = gridOrigin.X + (gridPixelSize - totalWidth) * 0.5f;
        
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.TextUnformatted(parts[0]);
        ImGui.SameLine(0, 2);
        drawList.AddRectFilled(new Vector2(ImGui.GetCursorScreenPos().X, y + 2), 
            new Vector2(ImGui.GetCursorScreenPos().X + 12, y + ImGui.GetTextLineHeight() - 2), UiColors.StatusError);
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X + 16, y));
        ImGui.TextUnformatted(parts[1]);
    }

    // --- Fields ---
    /// <summary>List of snake segment positions (head is first).</summary>
    private readonly List<(int X, int Y)> _snake = new();
    /// <summary>Current and next movement direction.</summary>
    private Direction _currentDirection = Direction.Right, _nextDirection = Direction.Right;
    /// <summary>Current food position.</summary>
    private int _foodX, _foodY;
    /// <summary>Current score, high score, and time since last update.</summary>
    private int _score, _highScore;
    private float _timeSinceLastUpdate;
    /// <summary>Current game state.</summary>
    private GameState _gameState = GameState.Menu;
}

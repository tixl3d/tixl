#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Framing and the fold transition: fitting a space into view, blending scopes and the Original↔Straight basis.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <param name="keepScope">Adopt the new framing without moving the view — for a size change the user
    /// caused themselves, where a refit reads as the canvas jumping out from under them.</param>
    /// <summary>
    /// Eases the rectify basis from the previously focused surface to the newly selected one. Blends on a
    /// private buffer so the stored quad is never touched, and only between two real surfaces — entering or
    /// leaving the rectified view snaps (there's nothing to turn from), as does an active edit.
    /// </summary>
    private Vector2[] BlendBasisTransition(Guid basisId, Vector2[] targetQuad, ref Vector2 targetSize, ref Vector2 targetAnchor, bool frozen)
    {
        // A lifted freeze is the same situation as a basis switch: an edge crop rewrote the quad, size, and
        // anchor R is built from, and they'd land in one frame — a view jump the user never asked for. Ease
        // from the frozen state instead, so the rectified view settles onto the edit.
        if (!frozen && _basisWasFrozen && basisId == _basisTransitionId && _basisHasLast)
        {
            for (var i = 0; i < 4; i++)
                _basisFromQuad[i] = _basisLastQuad[i];

            _basisFromSize = _basisLastSize;
            _basisFromAnchor = _basisLastAnchor;
            _basisMorph = 0f;

            // Same basis: the edit settles *inside* the held framing — the camera must not chase it.
            _easeKeepsFraming = true;
        }

        _basisWasFrozen = frozen;

        if (basisId != _basisTransitionId)
        {
            if (!frozen && _basisTransitionId != Guid.Empty && basisId != Guid.Empty && _basisHasLast)
            {
                for (var i = 0; i < 4; i++)
                    _basisFromQuad[i] = _basisLastQuad[i];

                _basisFromSize = _basisLastSize;
                _basisFromAnchor = _basisLastAnchor;
                _basisMorph = 0f;
            }
            else
            {
                _basisMorph = 1f;
            }

            // A different basis is a different rectified world — the framing re-derives (with the ease).
            _easeKeepsFraming = false;
            _basisTransitionId = basisId;
        }

        var resultQuad = targetQuad;
        if (_basisMorph < 1f && !frozen)
        {
            var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
            _basisMorph = MathF.Min(1f, _basisMorph + dt / _morphDuration);
            var t = MathF.Pow(_basisMorph, _morphEaseExponent);
            for (var i = 0; i < 4; i++)
                _basisBlendQuad[i] = Vector2.Lerp(_basisFromQuad[i], targetQuad[i], t);

            targetSize = Vector2.Lerp(_basisFromSize, targetSize, t);
            targetAnchor = Vector2.Lerp(_basisFromAnchor, targetAnchor, t);
            resultQuad = _basisBlendQuad;
        }

        // Remember the resolved basis, so a transition interrupted by another selection chains from here.
        for (var i = 0; i < 4; i++)
            _basisLastQuad[i] = resultQuad[i];

        _basisLastSize = targetSize;
        _basisLastAnchor = targetAnchor;
        _basisHasLast = true;
        return resultQuad;
    }

    /// <summary>
    /// The board rect (Y up) this output view settles on: the output card for Original, the rectified surface
    /// with its surround (inside the output card's space) for Straight, and the frame in flight otherwise (Content).
    /// </summary>
    private void GetSettledBoardRect(Setup setup, Guid basisId, Surface? basis, Surface.OutputMapping? basisMapping,
                                     Vector2 canvasSize, Vector2 viewSize, out Vector2 min, out Vector2 max)
    {
        if (_morphTarget < 0.5f || basis == null || basisMapping == null)
        {
            min = new Vector2(_spaceOrigin.X, _spaceOrigin.Y - canvasSize.Y / _spacePixelsPerMeter);
            max = new Vector2(_spaceOrigin.X + canvasSize.X / _spacePixelsPerMeter, _spaceOrigin.Y);
            return;
        }

        if (_morphTarget < 1.5f)
        {
            var span = _straightRectMax - _straightRectMin;
            var surround = new Vector2(MathF.Max(span.X, span.Y) * _straightSurroundFactor);
            var framedMin = _straightRectMin - surround;
            var framedMax = _straightRectMax + surround;
            min = new Vector2(_spaceOrigin.X + framedMin.X / _spacePixelsPerMeter, _spaceOrigin.Y - framedMax.Y / _spacePixelsPerMeter);
            max = new Vector2(_spaceOrigin.X + framedMax.X / _spacePixelsPerMeter, _spaceOrigin.Y - framedMin.Y / _spacePixelsPerMeter);
            return;
        }

        var topLeft = _projection.CanvasToBoard(Vector2.Zero);
        var bottomRight = _projection.CanvasToBoard(viewSize);
        min = new Vector2(topLeft.X, bottomRight.Y);
        max = new Vector2(bottomRight.X, topLeft.Y);
    }

    /// <summary>Frames a view-space area of <paramref name="size"/> px from the space's origin (a static space's whole extent).</summary>
    private void FitToArea(Vector2 size, EditModes mode, Guid outputId, bool keepScope = false)
    {
        var topLeft = _projection.CanvasToBoard(Vector2.Zero);
        var bottomRight = _projection.CanvasToBoard(size);
        FitToBoardRect(new Vector2(topLeft.X, bottomRight.Y), new Vector2(bottomRight.X, topLeft.Y), mode, outputId, keepScope);
    }

    /// <summary>Remembers the camera and the board rect it shows, so a starting transition eases from there.</summary>
    private void CaptureTransitionStart()
    {
        _probeCentreSamples.Clear();
        _morphFromScope = _boardCanvas.GetCurrentScope();
        var scale = new Vector2(MathF.Max(MathF.Abs(_morphFromScope.Scale.X), 0.0001f), MathF.Max(MathF.Abs(_morphFromScope.Scale.Y), 0.0001f));
        var canvasMin = _morphFromScope.Scroll;
        var canvasMax = canvasMin + _boardCanvas.WindowSize / scale;
        _morphFromMin = new Vector2(canvasMin.X, -canvasMax.Y);
        _morphFromMax = new Vector2(canvasMax.X, -canvasMin.Y);
    }

    /// <param name="min">Board metres, Y up, of what the settled view frames.</param>
    private void FitToBoardRect(Vector2 min, Vector2 max, EditModes mode, Guid outputId, bool keepScope = false)
    {
        var size = max - min;
        var key = (outputId, mode, size);

        // A different framed canvas shows different handles — the sub-element plane can't carry over.
        if (_fitKey.Item1 != outputId || _fitKey.Item2 != mode)
            _canvasSelection.Clear();

        // Folding back to the Board: the camera is on its way to the remembered Board view, not to a fit.
        if (_spaceTarget <= 0f)
        {
            _fitKey = key;
            return;
        }

        // While the view morphs — or the space is still coming in from the Board — ease the camera from
        // wherever the user had it to the fit for the current framing. Snapping straight to the fit (as we do
        // at rest) would throw their view away the instant a transition starts — the scale/offset has to
        // animate along with everything else.
        // One easing for camera and geometry: the framed rect itself is interpolated from the view the user
        // had to the settled rect (already carrying its margin), and the camera simply shows that rect.
        var progress = MathF.Min(MathF.Min(_morphProgress, _spaceProgress), MathF.Min(_referenceProgress, _referenceSubjectProgress));
        if (progress >= 1f)
            ReportTransitionMetrics();

        if (progress < 1f)
        {
            InflateByScreenMargin(ref min, ref max);
            var eased = MathF.Pow(progress, _morphEaseExponent);
            var scope = BlendScopes(_morphFromScope, ScopeShowing(min, max), eased);
            _boardCanvas.SetScopeInstant(scope);
            _fitKey = key;
            return;
        }

        // Instant fit whenever the framed area changes (output, mode, or content size) — no jump-then-settle.
        if (_fitKey == key)
            return;

        if (keepScope)
        {
            _fitKey = key;
            return;
        }

        InflateByScreenMargin(ref min, ref max);
        _boardCanvas.SetScopeInstant(FitScope(min, max));
        _fitKey = key;
    }

    private void SampleTransitionMetrics()
    {
        if (MathF.Min(MathF.Min(_morphProgress, _spaceProgress), MathF.Min(_referenceProgress, _referenceSubjectProgress)) >= 1f)
            return;

        _probeCentreSamples.Add(_probeSurfaceCentre - (_boardCanvas.WindowPos + _boardCanvas.WindowSize * 0.5f));
    }

    private void ReportTransitionMetrics()
    {
        if (_probeCentreSamples.Count < 2)
        {
            _probeCentreSamples.Clear();
            return;
        }

        var first = _probeCentreSamples[0];
        var last = _probeCentreSamples[^1];
        var chord = last - first;
        var chordLength = MathF.Max(chord.Length(), 0.001f);
        var path = 0f;
        var sumDistance = 0f;
        var maxDeviation = 0f;
        for (var i = 0; i < _probeCentreSamples.Count; i++)
        {
            var point = _probeCentreSamples[i];
            sumDistance += point.Length();
            if (i > 0)
                path += (point - _probeCentreSamples[i - 1]).Length();

            // Distance from the chord line.
            var rel = point - first;
            var deviation = MathF.Abs(rel.X * chord.Y - rel.Y * chord.X) / chordLength;
            maxDeviation = MathF.Max(maxDeviation, deviation);
        }

        T3.Core.Logging.Log.Debug($"[fold] metrics mode={_editMode} samples={_probeCentreSamples.Count} meanDistFromCentre={sumDistance / _probeCentreSamples.Count:0} px "
                                  + $"pathOverChord={path / chordLength:0.00} maxChordDeviation={maxDeviation:0} px start={first} end={last}");
        _probeCentreSamples.Clear();
    }

    /// <summary>The scope the Board camera would take to show a board rect (Y up), centred — pure, nothing set.</summary>
    private CanvasScope ScopeShowing(Vector2 min, Vector2 max)
    {
        var areaMin = new Vector2(min.X, -max.Y);
        var areaSize = new Vector2(MathF.Max(max.X - min.X, 0.0001f), MathF.Max(max.Y - min.Y, 0.0001f));
        var window = _boardCanvas.WindowSize;
        float scale;
        Vector2 scroll;
        if (areaSize.X / areaSize.Y > window.X / window.Y)
        {
            scale = window.X / areaSize.X;
            scroll = new Vector2(areaMin.X, areaMin.Y - (window.Y / scale - areaSize.Y) / 2);
        }
        else
        {
            scale = window.Y / areaSize.Y;
            scroll = new Vector2(areaMin.X - (window.X / scale - areaSize.X) / 2, areaMin.Y);
        }

        return new CanvasScope { Scale = new Vector2(scale, scale), Scroll = scroll };
    }

    /// <summary>
    /// A camera in flight between two scopes: the zoom is interpolated geometrically (a zoom is a ratio, so
    /// the eye sees a steady rate) and the point at the window's centre linearly.
    /// </summary>
    private CanvasScope BlendScopes(CanvasScope from, CanvasScope to, float t)
    {
        var fromScale = MathF.Max(MathF.Abs(from.Scale.X), 0.0001f);
        var toScale = MathF.Max(MathF.Abs(to.Scale.X), 0.0001f);
        var scale = fromScale * MathF.Pow(toScale / fromScale, t);
        var halfWindow = _boardCanvas.WindowSize * 0.5f;
        var fromCentre = from.Scroll + halfWindow / fromScale;
        var toCentre = to.Scroll + halfWindow / toScale;
        var centre = Vector2.Lerp(fromCentre, toCentre, t);
        return new CanvasScope { Scale = new Vector2(scale, scale), Scroll = centre - halfWindow / scale };
    }

    /// <summary>A board point (Y up) on screen under a given camera scope.</summary>
    private Vector2 BoardToScreen(Vector2 board, CanvasScope scope)
    {
        var canvas = new Vector2(board.X, -board.Y);
        return (canvas - scope.Scroll) * scope.Scale + _boardCanvas.WindowPos;
    }

    /// <summary>The Board camera showing a board rect (Y up), centred, the canvas' own Y-down convention applied.</summary>
    private CanvasScope FitScope(Vector2 min, Vector2 max)
    {
        _boardCanvas.FitAreaOnCanvas(new ImRect(new Vector2(min.X, -max.Y), new Vector2(max.X, -min.Y)));
        return _boardCanvas.GetTargetScope();
    }

    /// <summary>
    /// Grows a board rect by a small screen-space margin so a surface that overhangs the output (common in
    /// Content/Output views) isn't jammed against the window edge.
    /// </summary>
    private void InflateByScreenMargin(ref Vector2 min, ref Vector2 max)
    {
        var scale = MathF.Abs(ScopeShowing(min, max).Scale.X);
        if (scale <= 0.0001f)
            return;

        var margin = new Vector2(10 * T3Ui.UiScaleFactor / scale);
        min -= margin;
        max += margin;
    }
}

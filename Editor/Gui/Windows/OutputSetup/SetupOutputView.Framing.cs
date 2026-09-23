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
        if (!frozen && _hold.BasisWasFrozen && basisId == _basisTransitionId && _hold.BasisHasLast)
        {
            StartBasisEaseFromLast();

            // Same basis: the edit settles *inside* the held framing — the camera must not chase it.
            _hold.EaseKeepsFraming = true;
        }

        _hold.BasisWasFrozen = frozen;

        if (basisId != _basisTransitionId)
        {
            if (!frozen && _basisTransitionId != Guid.Empty && basisId != Guid.Empty && _hold.BasisHasLast)
                StartBasisEaseFromLast();
            else
                _basisEase.Settle();

            // A different basis is a different rectified world — the framing re-derives (with the ease).
            _hold.EaseKeepsFraming = false;
            _basisTransitionId = basisId;
        }

        var resultQuad = targetQuad;
        if (!_basisEase.IsSettled && !frozen)
        {
            _basisEase.Advance(FrameDeltaSec(), MorphDurationSec, MorphEaseExponent);
            var t = _basisEase.Value;
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
        _hold.BasisHasLast = true;
        return resultQuad;
    }

    /// <summary>Eases the basis from wherever it was last resolved, so an interrupted turn chains instead of jumping.</summary>
    private void StartBasisEaseFromLast()
    {
        for (var i = 0; i < 4; i++)
            _basisFromQuad[i] = _basisLastQuad[i];

        _basisFromSize = _basisLastSize;
        _basisFromAnchor = _basisLastAnchor;
        _basisEase.Restart();
    }

    /// <summary>
    /// The board rect (Y up) this output view settles on: the output card for Original, the rectified surface
    /// with its surround (inside the output card's space) for Straight.
    /// </summary>
    private void GetSettledBoardRect(Surface? basis, Surface.OutputMapping? basisMapping, Vector2 canvasSize, out Vector2 min, out Vector2 max)
    {
        if (_viewMorph.Target < 0.5f || basis == null || basisMapping == null)
        {
            min = new Vector2(_spaceOrigin.X, _spaceOrigin.Y - canvasSize.Y / _spacePixelsPerMeter);
            max = new Vector2(_spaceOrigin.X + canvasSize.X / _spacePixelsPerMeter, _spaceOrigin.Y);
            return;
        }

        var span = _framing.StraightMax - _framing.StraightMin;
        var surround = new Vector2(MathF.Max(span.X, span.Y) * RectifiedFraming.StraightSurroundFactor);
        var framedMin = _framing.StraightMin - surround;
        var framedMax = _framing.StraightMax + surround;
        min = new Vector2(_spaceOrigin.X + framedMin.X / _spacePixelsPerMeter, _spaceOrigin.Y - framedMax.Y / _spacePixelsPerMeter);
        max = new Vector2(_spaceOrigin.X + framedMax.X / _spacePixelsPerMeter, _spaceOrigin.Y - framedMin.Y / _spacePixelsPerMeter);
    }

    /// <summary>Remembers the camera, so a starting transition eases from there.</summary>
    private void CaptureTransitionStart()
    {
        _morphFromScope = _boardCanvas.GetCurrentScope();
    }

    /// <param name="min">Board metres, Y up, of what the settled view frames.</param>
    /// <param name="keepScope">Adopt the new framing without moving the view — for a size change the user
    /// caused themselves, where a refit reads as the canvas jumping out from under them.</param>
    private void FitToBoardRect(Vector2 min, Vector2 max, EditModes mode, Guid outputId, bool keepScope = false)
    {
        var size = max - min;
        var key = (outputId, mode, size);

        // A different framed canvas shows different handles — the sub-element plane can't carry over.
        if (_fitKey.OutputId != outputId || _fitKey.Mode != mode)
            _canvasSelection.Clear();

        // Folding back to the Board: the camera is on its way to the remembered Board view, not to a fit.
        if (_spaceBlend.Target <= 0f)
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
        var progress = MathF.Min(MathF.Min(_viewMorph.Progress, _spaceBlend.Progress),
                                 MathF.Min(_referenceStraighten.Progress, _referenceSubjectEase.Progress));
        if (progress < 1f)
        {
            InflateByScreenMargin(ref min, ref max);
            var eased = MathF.Pow(progress, MorphEaseExponent);
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

    /// <summary>The Board camera showing a board rect (Y up), centred, the canvas' own Y-down convention applied.</summary>
    private CanvasScope FitScope(Vector2 min, Vector2 max)
    {
        _boardCanvas.FitAreaOnCanvas(new ImRect(new Vector2(min.X, -max.Y), new Vector2(max.X, -min.Y)));
        return _boardCanvas.GetTargetScope();
    }

    /// <summary>
    /// Grows a board rect by a small screen-space margin so a surface that overhangs the output (common on
    /// the Output canvas) isn't jammed against the window edge.
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

    // Basis transition: eases the rectify basis (quad/size/anchor) from the previously focused surface to the
    // newly selected one, so switching selection in a rectified view turns the scene rather than snapping.
    private readonly Vector2[] _basisFromQuad = new Vector2[4];
    private readonly Vector2[] _basisLastQuad = new Vector2[4];
    private readonly Vector2[] _basisBlendQuad = new Vector2[4];
    private Vector2 _basisFromSize, _basisLastSize, _basisFromAnchor, _basisLastAnchor;
    private Guid _basisTransitionId;
    private EasedValue _basisEase = EasedValue.Settled(1f);

    // Rectified framing: this frame's R and window, and the hold that keeps the window still across frames.
    private RectifiedFraming _framing;
    private RectifiedFraming.FramingHold _hold;
}

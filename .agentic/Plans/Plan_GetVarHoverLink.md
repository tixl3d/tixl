# Show Set/Get-Var link when hovering a Get<Type>Var operator

Ticket: #1077 — https://github.com/tixl3d/tixl/issues/1077
Size: —   Milestone: v4.2

## Problem
The virtual link between a `Set<Type>Var` and its matching `Get<Type>Var` is currently drawn only when
hovering the **Set**Var operator. It should also appear when hovering the **Get**Var operator, so the user
can trace a variable from either end.

## Affected code
- Generic link drawer: `Editor/Gui/OpUis/OpUi.cs:129-157` — `DrawVariableReferences(drawList, canvas,
  startCenter, sourceInstance, variableName, symbolId, variableNameInputId)`. It scans the parent's children
  for ones whose `Symbol.Id == symbolId` and whose `variableNameInputId` input value equals `variableName`,
  and draws a line to each. It is **direction-agnostic** — it just links a source instance to siblings of a
  given symbol sharing the variable name.
- Existing caller (Set side, hover-gated): `Editor/Gui/OpUis/UIs/SetFloatVarUi.cs:40-45`
  ```csharp
  if (area.Contains(ImGui.GetMousePos()))
      OpUi.DrawVariableReferences(drawList, canvas, area.GetCenter(), instance, data.VariableName.Value,
                                  /*GetFloatVar symbolId*/, /*GetFloatVar varName input*/);
  ```
  Same pattern in `SetStringVarUi`, `SetIntVarUi`, `SetBoolVarUi`, `SetVec3VarUi`.
- Get side (no link today): `GetFloatVarUi.cs`, `GetStringVarUi.cs`, `GetIntVarUi.cs`, `GetBoolVarUi.cs`,
  `GetVec3VarUi.cs`. Each has a `VariableName` bound input (e.g. GetFloatVar var-name input
  `015d1ea0-ea51-4038-893a-4af2f8584631`).
- Symbol GUIDs for every Set*/Get* pair are listed in the registration table at `OpUi.cs:~108-126`.

## Proposed approach
Add the same hover block to each `Get<Type>VarUi.DrawChildUi`, calling `DrawVariableReferences` with the
*Set* side's identifiers (the inverse of the Set caller): pass the GetVar instance as source, its
`VariableName.Value`, and the corresponding **Set<Type>Var** symbol GUID + that SetVar's variable-name input
GUID. Because the drawer is symmetric, this lights the link from a hovered GetVar to its SetVar(s).

Per type, the exact wiring needs two GUIDs (Set symbol id, Set var-name input id) gathered from each
`Set<Type>VarUi` and the `OpUi.cs` table.

## Risks / side-effects
- Low severity, but **wrong-GUID failures are silent** — a mistyped pair simply draws no line, with no
  compile error. That's the main reason this is a plan: correctness is only confirmable by hovering each of
  the 5 Get ops in the running editor, which an unattended run can't verify.
- Touches 5 `Get*VarUi` files (additive only). Keep the block identical to the Set side for consistency.

## Open questions
- Confirm each Set*Var's variable-name input GUID (may differ from the Get side's input GUID) before wiring.
- Optional polish: dedupe if both ends are hovered/selected so the link isn't drawn twice; pick whether
  hover on Get should also respect the existing selection-based drawing.

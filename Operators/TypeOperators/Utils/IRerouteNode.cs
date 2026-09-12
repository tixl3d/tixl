namespace Types.Routing;

/*
 * Marks built-in routing anchors for compact drawing and gesture-based creation in the Editor.
 * Implementations expose one plain InputSlot<T> and Slot<T> of the same type with no child operators.
 * The Editor validates that shape and resolves this marker by name from the loaded package assembly.
 */
public interface IRerouteNode
{
}

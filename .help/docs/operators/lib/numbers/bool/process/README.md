# Lib.numbers.bool.process

## Operators

- [**CacheBoolean**](CacheBoolean.md) — Prevents multiple updates by forwarding a boolean value that was computed earlier.
- [**DelayBoolean**](DelayBoolean.md)
- [**DelayTriggerChange**](DelayTriggerChange.md) — Delays the change of a boolean flag. This can be useful for implementing interactions where a value needs to stay true for a minimum duration. In "DelayTrue" mode, it will immediately switch to true but delay switching back to false. Note: This is NOT a queue. Frequent changes of the incoming signal can lead to the delayed state filtering out changes within the delay duration. In vvvv, this op is called a MonoFlop.
- [**KeepBoolean**](KeepBoolean.md) — Keeps the state of flag until it is reset.

---

*Auto-generated from the operator library.*

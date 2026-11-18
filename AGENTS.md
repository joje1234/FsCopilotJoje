# FS Copilot project guidance

- Before adding aircraft-specific code, check vanilla FS Copilot's implementation
  and the aircraft's complete definition tree, including included modules.
- Reuse the existing YAML loader, expressions, setters and working mappings.
  Keep aircraft differences in definitions. Add custom runtime code only where
  vanilla lacks the required behavior, such as per-group ownership and hardware
  alignment. Record why an exception is necessary.
- The user's current altimeter policy is normal shared operation of both separate
  instruments. Fix synchronization defects in the definitions/core; do not add
  pilot/copilot or autopilot ownership restrictions to the barometers.
- The tested installed EFB baseline is recorded in `HANDOFF.md`. Do not remove a
  working fallback before its definition-driven replacement is validated, or
  describe local/fake tests as actual two-PC simulator validation.

# ADR 0004: Packaging and explicit verification boundaries

Status: accepted.

The default container is multi-stage and framework-dependent. The build stage pins SDK 10.0.402; the runtime uses .NET 10, invariant globalization and the image's non-root APP_UID. Publishing disables the apphost so the output is portable managed IL for the runtime container. Credentials are supplied only at runtime.

The full CLI was published and runtime-smoke-tested as both framework-dependent and Native AOT for `win-x64`. Both outputs returned the expected `perft 5` node count and completed a UCI/`isready` handshake. This evidence covers only Windows x64; it does not validate other RIDs or an AOT container. Do not describe the container as AOT.

Docker image build/runtime verification remains unrun; the Dockerfile is reviewed but not validated in an image. CI executes restore, Release build, MTP tests and the deterministic CLI bench. A hosted CI run is not claimed by local validation.

The plan's magic-attack implementation is replaced by occupancy subset lookup with guarded PEXT plus software fallback; see ADR 0001. Position is a reference type owning fixed mutable state rather than a copied struct. Default benchmark depth is five, not the illustrative twelve, to keep CI bounded; callers can select deeper searches. Search depth is explicitly capped at 94 plies.

Live Lichess play and account upgrade remain human-operated. Automated fixtures are sanitized representative protocol payloads, not recordings made with a real credential. The manual live scenario is documented in README. This verification boundary must remain explicit in release notes until a human records a live pass.

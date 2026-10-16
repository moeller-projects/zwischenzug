# ADR 0004: Framework-dependent packaging and explicit verification boundaries

Status: accepted.

The default container is multi-stage and framework-dependent. The build stage pins SDK 10.0.402; the runtime uses .NET 10, invariant globalization and the image's non-root APP_UID. Publishing disables the apphost so the output is portable managed IL for the runtime container. Credentials are supplied only at runtime.

Native AOT was not attempted: only framework-dependent publish was authorized, and native toolchain/container validation is not available in this environment. Libraries declare IsAotCompatible, and Lichess uses source-generated JSON, but these are not evidence that the full CLI has passed Native AOT publishing. Do not describe the container as AOT. Enabling AOT requires a separately authorized publish on a supported native toolchain and review of every trim/AOT diagnostic.

Docker is not installed here, so the Dockerfile can be reviewed but no image build or container runtime pass is claimed. The framework-dependent artifact is verified locally after publish. CI executes restore, Release build, MTP tests and the deterministic CLI bench. A hosted CI run is not claimed by local validation.

The plan's magic-attack implementation is replaced by occupancy subset lookup with guarded PEXT plus software fallback; see ADR 0001. Position is a reference type owning fixed mutable state rather than a copied struct. Default benchmark depth is five, not the illustrative twelve, to keep CI bounded; callers can select deeper searches. Search depth is explicitly capped at 94 plies.

Live Lichess play and account upgrade remain human-operated. Automated fixtures are sanitized representative protocol payloads, not recordings made with a real credential. The manual live scenario is documented in README. This verification boundary must remain explicit in release notes until a human records a live pass.

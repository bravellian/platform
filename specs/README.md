# Outbox Specification Files - Index

This directory contains the Outbox Component specification and related documentation.

## Main Specification

**[outbox-specification.md](outbox-specification.md)**  
The complete functional specification for the Outbox component. This is the authoritative source for Outbox behavior, API surface, and requirements.

**Last Updated**: 2025-12-07  
**Version**: 1.0  
**Status**: Active - Join-agnostic, with comprehensive parameter semantics

### Key Characteristics:
- Join-agnostic (all join/fan-in coordination removed)
- Comprehensive parameter documentation
- Testable behavioral requirements (OBX-001 through OBX-130)
- Canonical method signatures with implementation notes for overloads

## Supporting Documentation

### Change Summary

**[SPEC_UPDATE_SUMMARY.md](SPEC_UPDATE_SUMMARY.md)**  
Comprehensive summary of changes made to separate join concerns and normalize parameter semantics.

**Purpose**: Quick reference for understanding what changed and why.

### Future Join Specification

**[join-coordination-specification.md](join-coordination-specification.md)**  
Outline and content for the separate Join Coordination Component specification.

**Purpose**: Ready-to-use template for creating the independent Join spec with all content extracted from Outbox.

**Contains**:
- All join concepts (JoinIdentifier, Join, Join Member, etc.)
- Join API methods (StartJoinAsync, AttachMessageToJoinAsync, etc.)
- Join requirements (JOIN-001 through JOIN-092, renumbered from OBX-xxx)
- Join database schema (OutboxJoin, OutboxJoinMember tables)
- Join usage examples
- Join-specific open questions

## AI Prompts for Specification Work

These prompts can be used to validate changes or apply similar updates to other specifications.

### Extract Join Specification

**[prompt-extract-join-spec.md](prompt-extract-join-spec.md)**  
Complete instructions for extracting join/fan-in coordination from the Outbox spec and creating a separate Join Coordination specification.

**Use cases**:
- Validating the separation of concerns
- Creating the actual join-coordination-specification.md file
- Understanding the architectural boundary between Outbox and Join

### Normalize Parameter Semantics

**[prompt-normalize-parameters.md](prompt-normalize-parameters.md)**  
Complete instructions for normalizing method signatures and adding comprehensive parameter documentation to any specification.

**Use cases**:
- Applying the same parameter precision to other component specs
- Validating that all parameters are fully documented
- Ensuring testable behavioral requirements exist for all constraints

## Quick Navigation

### By Topic

- **Architecture & Scope**: [outbox-specification.md §2-3](outbox-specification.md#2-purpose-and-scope)
- **Key Concepts**: [outbox-specification.md §4](outbox-specification.md#4-key-concepts-and-terms)
- **API Surface**: [outbox-specification.md §5](outbox-specification.md#5-public-api-surface)
- **Behavioral Requirements**: [outbox-specification.md §6](outbox-specification.md#6-behavioral-requirements)
- **Database Schema**: [outbox-specification.md Appendix A](outbox-specification.md#appendix-a-database-schema-reference)
- **Usage Examples**: [outbox-specification.md Appendices B-C](outbox-specification.md#appendix-b-handler-implementation-example)

### By Use Case

- **Understanding what changed**: See [SPEC_UPDATE_SUMMARY.md](SPEC_UPDATE_SUMMARY.md)
- **Creating Join spec**: Use [join-coordination-specification.md](join-coordination-specification.md) and [prompt-extract-join-spec.md](prompt-extract-join-spec.md)
- **Implementing the Outbox**: See [outbox-specification.md](outbox-specification.md) §5-6
- **Deploying the schema**: See [outbox-specification.md](outbox-specification.md) Appendix A
- **Writing handlers**: See [outbox-specification.md](outbox-specification.md) Appendix B
- **Multi-tenant setup**: See [outbox-specification.md](outbox-specification.md) Appendix C

## Change History

### 2025-12-07: Major Update

**Changes**:
1. Separated join/fan-in coordination from Outbox (now architecturally independent)
2. Added comprehensive parameter semantics to all public interfaces
3. Added testable behavioral requirements for parameter constraints
4. Defined canonical method signatures with implementation notes for overloads
5. Updated all examples to match canonical signatures

**Requirements Added**: OBX-127, OBX-128, OBX-129, OBX-130  
**Requirements Removed**: OBX-033, OBX-034, OBX-046, OBX-047, OBX-080 through OBX-096 (moved to Join spec)  
**Sections Removed**: §4.5 Join/Fan-In Concepts, §6.11 Join/Fan-In Coordination, Appendix D Join Example

See [SPEC_UPDATE_SUMMARY.md](SPEC_UPDATE_SUMMARY.md) for complete details.

---

**File Count**: 5 specification-related files  
**Total Size**: ~74 KB  
**Maintained By**: Bravellian Platform Team

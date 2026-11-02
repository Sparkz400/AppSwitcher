# AppSwitcher

<!-- impeccable:product-schema 1 -->

## Platform

Native Windows desktop application, implemented in WPF on .NET 10.

## Users and operating context

Operators use touch panels to open assigned applications on industrial workstations. Maintenance staff use Windows desktops to configure applications, investigate faults, and manage access. Both contexts were confirmed by the owner.

## Purpose

Provide predictable application switching and supervised application lifecycle management without obscuring the applications people are operating.

## Capabilities and constraints

- Existing application launch, foreground activation, groups, favorites, schedules, dependencies, lifecycle hooks, window policies, and saved layouts remain available.
- Existing operator scopes, administrator permissions, PIN authentication, session timeout, kiosk enforcement, watchdog acknowledgement, configuration validation, backups, import/export, and audit recording remain authoritative.
- Configuration compatibility and customer data must be preserved.
- English and German, keyboard and touch operation, light and dark appearance, and Windows display scaling are supported presentation requirements.
- Application health describes software state; it does not establish machine safety.

## Product principles

1. Keep frequent operating actions visible and stable.
2. Put application state, explanation, and the permitted action together.
3. Separate daily operation from administration without inventing new permission roles.
4. Use Windows controls, accessibility, and window behavior consistently.

## Evidence

The repository contains the operational implementation and regression tests. Hardware-specific glove use and customer usability require trials on the intended panels; desktop automation is not a substitute for those trials.

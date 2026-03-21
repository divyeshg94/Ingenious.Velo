```markdown
# Design System Specification: High-End Data Editorial

## 1. Overview & Creative North Star: "The Analytical Architect"

This design system is engineered for the high-stakes environment of Azure DevOps. While it respects the utility of the host platform, it rejects the "utility-first" clutter often found in developer tools. Our Creative North Star is **The Analytical Architect**. 

We move beyond the "standard dashboard" by treating data-intensive views as editorial layouts. Instead of a sea of borders and boxes, we use **intentional asymmetry**, **tonal depth**, and **rhythmic spacing** to guide the eye. The goal is to make complex DevOps data feel not just accessible, but authoritative and curated. We achieve a premium feel by stripping away the "noise" of traditional UI (lines, heavy shadows) and replacing it with sophisticated surface layering.

---

## 2. Colors & Surface Philosophy

The palette is rooted in Azure’s DNA but elevated through a refined tonal range. We prioritize "Surface over Stroke."

### The "No-Line" Rule
**Prohibit 1px solid borders for sectioning.** Traditional dividers create visual friction. Boundaries must be defined solely through background color shifts or subtle tonal transitions. A `surface-container-low` section sitting on a `surface` background provides enough contrast to signify a boundary without the "grid-lock" feel of lines.

### Surface Hierarchy & Nesting
Treat the UI as a physical stack of fine paper. 
- **Base Level:** `surface` (#faf9f8)
- **Primary Layout Sections:** `surface-container-low` (#f4f3f2)
- **Interactive Cards/Modules:** `surface-container-lowest` (#ffffff)
- **Active/Hovered States:** `surface-container-high` (#e9e8e7)

### The "Glass & Gradient" Rule
To escape the "flat" enterprise look, use **Glassmorphism** for transient elements (modals, dropdowns, floating toolbars). Use semi-transparent `surface` colors with a `backdrop-blur` of 12px–20px. 
**Signature Gradients:** For high-impact CTA buttons or progress indicators, utilize a subtle linear gradient from `primary` (#005faa) to `secondary_container` (#5badff) at a 135° angle to provide a "lit from within" professional polish.

---

## 3. Typography: Editorial Authority

We use **Inter** (as specified in the scale) to provide a modern, neutral canvas that improves legibility over standard system fonts at small sizes.

- **Display & Headlines:** Use `display-sm` and `headline-md` to create clear entry points. These should have a tighter letter-spacing (-0.02em) to feel "locked in" and authoritative.
- **Data Points:** Use `label-md` for metadata. In data-heavy tables, use `on_surface_variant` (#404752) to keep secondary information from competing with primary titles.
- **The Hierarchy Rule:** Never use two different font sizes that are only 1pt apart. Jump scales (e.g., pairing `title-lg` with `body-sm`) to create immediate visual hierarchy that helps developers scan logs and metrics faster.

---

## 4. Elevation & Depth: Tonal Layering

Traditional elevation uses shadows to simulate height; we use color to simulate **proximity**.

- **The Layering Principle:** Place a `surface-container-lowest` card on a `surface-container-low` section. This creates a soft, natural "lift."
- **Ambient Shadows:** For floating elements like Tooltips or Context Menus, use a "Ghost Shadow": 
  - `box-shadow: 0 8px 32px rgba(26, 28, 28, 0.06);` (Using a 6% opacity of the `on_surface` color).
- **The "Ghost Border" Fallback:** If a border is required for accessibility in high-contrast modes, use `outline-variant` (#c0c7d4) at 20% opacity. 100% opaque borders are strictly forbidden.

---

## 5. Components

### Buttons
- **Primary:** Gradient fill (`primary` to `primary_container`), `roundness-md` (0.375rem). Text: `on_primary` (#ffffff).
- **Secondary:** Surface-tinted. Background: `primary_fixed_dim` (#a3c9ff) at 30% opacity. No border.
- **Tertiary:** Text-only using `on_primary_fixed_variant` (#004883).

### Cards & Data Lists
- **Rule:** Forbid the use of divider lines.
- **Implementation:** Separate list items using `spacing-2.5` (0.5rem) of vertical white space. Use a subtle background hover state of `surface_container_highest` (#e3e2e1) to indicate interactivity.
- **Status Indicators:** Use `tertiary_container` (#bc5b00) for warnings and `error` (#ba1a1a) for critical failures. Apply these as "Status Pills" with `roundness-full` and `label-sm` typography.

### Input Fields
- **Aesthetic:** Minimalist. Use `surface_container_low` as the background fill.
- **Active State:** Instead of a thick border, use a 2px bottom-accent in `primary` (#005faa) and a subtle shift to `surface_container_lowest`.

### Data Visualization (Signature Component)
- **Heatmaps/Sparklines:** Use the `secondary` to `secondary_container` range. 
- **Interactive Tooltips:** Use Glassmorphism (Backdrop-blur 16px) with `on_surface` text. Ensure the tooltip "floats" with an Ambient Shadow.

---

## 6. Do’s and Don’ts

### Do
- **Do** use `spacing-8` or `spacing-10` between major data sections to allow the UI to "breathe."
- **Do** use `surface_bright` to highlight the most critical "Active" work item in a list.
- **Do** use the `roundness-xl` (0.75rem) for large dashboard containers to soften the "industrial" feel of Azure DevOps.

### Don't
- **Don’t** use black (#000000). Use `on_background` (#1a1c1c) for all "black" text to maintain tonal harmony.
- **Don’t** use standard 1px borders to separate table rows. Use alternating row tints (`surface` vs `surface_container_low`).
- **Don’t** use high-saturation red for errors. Use the specified `error` (#ba1a1a) which is tuned for professional, long-term viewing.
- **Don’t** crowd the interface. If the data is intensive, increase the spacing scale rather than decreasing the font size.```
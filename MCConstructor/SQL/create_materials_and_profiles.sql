-- ============================================================================
-- Migration: create_materials_and_profiles
--
-- Adds the per-project material library used by nesting, plus the profiles
-- (structural T, bulb flat, ...) that reference a cross-section .dwg.
--
-- Tables added:
--   public.materials         - steel grades + density per project
--   public.material_plates   - stock plate sizes per material (used by nesting)
--   public.profiles          - extrudable cross-sections per project
--
-- Tables modified:
--   public.drawings          - 'Profile' added to drawing_type CHECK list so
--                              cross-section drawings can be registered.
--
-- Run AFTER create_drawings_table.sql.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1) MATERIALS
-- A material is "DH-36 with density 7850 kg/m^3" etc. Scoped per-project so
-- different jobs can have different supplier specs without bleeding into
-- each other.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.materials (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id  UUID NOT NULL REFERENCES public.projects(id) ON DELETE CASCADE,

    name        VARCHAR(100) NOT NULL,
    description TEXT,

    -- Density in kg/m^3. 7850 (mild steel) is a sensible default; the dialog
    -- pre-fills it but the user is free to edit.
    density     DOUBLE PRECISION NOT NULL DEFAULT 7850,

    created_at  TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at  TIMESTAMP WITH TIME ZONE DEFAULT NOW(),

    CONSTRAINT chk_materials_density       CHECK (density > 0),
    CONSTRAINT uq_materials_project_name   UNIQUE (project_id, name)
);

CREATE INDEX IF NOT EXISTS idx_materials_project_id ON public.materials(project_id);

-- ----------------------------------------------------------------------------
-- 2) MATERIAL_PLATES
-- A material can have multiple stock plate sizes (e.g. DH-36 in 8x4, 10x5, ...).
-- Each plate gets a short code (T04, T05, ...) used in the nesting picker.
-- Dimensions are stored in millimetres so they round-trip cleanly with the
-- existing nesting code (which works in mm throughout).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.material_plates (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    material_id  UUID NOT NULL REFERENCES public.materials(id) ON DELETE CASCADE,

    code         VARCHAR(50) NOT NULL,   -- e.g. T04
    width        DOUBLE PRECISION NOT NULL,   -- mm
    height       DOUBLE PRECISION NOT NULL,   -- mm
    thickness    DOUBLE PRECISION NOT NULL,   -- mm
    description  TEXT,

    -- One plate per material may be flagged as the default - the nesting
    -- dialog selects this entry on open if present.
    is_default   BOOLEAN NOT NULL DEFAULT FALSE,

    created_at   TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at   TIMESTAMP WITH TIME ZONE DEFAULT NOW(),

    CONSTRAINT chk_plates_dims          CHECK (width > 0 AND height > 0 AND thickness > 0),
    CONSTRAINT uq_plates_material_code  UNIQUE (material_id, code)
);

CREATE INDEX IF NOT EXISTS idx_plates_material_id ON public.material_plates(material_id);

-- ----------------------------------------------------------------------------
-- 3) PROFILES
-- A profile (structural T, bulb flat, angle, ...) is defined by a 2D
-- cross-section drawing plus a material. The cross-section .dwg lives at
-- <project>/01 Standards/Profiles/<name>.dwg and is registered in
-- public.drawings with drawing_type = 'Profile'. We FK to drawings so the
-- profile follows file moves / renames the same way other registered
-- drawings do.
--
-- density_override lets the user pin a specific density for a profile that
-- doesn't quite match its material's nominal density (e.g. plated, coated,
-- or supplier-specific runs). When NULL, the material's density is used.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.profiles (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id         UUID NOT NULL REFERENCES public.projects(id) ON DELETE CASCADE,

    name               VARCHAR(100) NOT NULL,    -- e.g. "T 200x10/100x15"
    code               VARCHAR(50),              -- short id used on drawings
    description        TEXT,

    material_id        UUID REFERENCES public.materials(id) ON DELETE SET NULL,
    drawing_id         UUID REFERENCES public.drawings(id)  ON DELETE SET NULL,

    -- kg/m^3 override. NULL -> use materials.density at lookup time.
    density_override   DOUBLE PRECISION,

    -- Optional cached cross-section area in mm^2. Computed by the plugin
    -- when the .dwg is parsed and stored here so weight calculations don't
    -- have to re-open the cross-section drawing every time.
    cross_section_area DOUBLE PRECISION,

    created_at         TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at         TIMESTAMP WITH TIME ZONE DEFAULT NOW(),

    CONSTRAINT chk_profiles_density_override CHECK (density_override IS NULL OR density_override > 0),
    CONSTRAINT chk_profiles_area             CHECK (cross_section_area IS NULL OR cross_section_area > 0),
    CONSTRAINT uq_profiles_project_name      UNIQUE (project_id, name)
);

CREATE INDEX IF NOT EXISTS idx_profiles_project_id  ON public.profiles(project_id);
CREATE INDEX IF NOT EXISTS idx_profiles_material_id ON public.profiles(material_id);
CREATE INDEX IF NOT EXISTS idx_profiles_drawing_id  ON public.profiles(drawing_id);

-- ----------------------------------------------------------------------------
-- 4) DRAWINGS - allow drawing_type = 'Profile'
-- Drop and re-add the CHECK constraint with 'Profile' included. Existing
-- rows are unaffected because the constraint is satisfied by all current
-- values.
-- ----------------------------------------------------------------------------
ALTER TABLE public.drawings DROP CONSTRAINT IF EXISTS chk_drawings_drawing_type;
ALTER TABLE public.drawings ADD CONSTRAINT chk_drawings_drawing_type CHECK (drawing_type IN (
    'BlockLibrary',
    'FunctionalDrawing',
    'DetailDrawing',
    'Template',
    'Titleblock',
    'Sheet',
    'Profile',
    'Other'
));

-- ----------------------------------------------------------------------------
-- 5) Timestamp triggers
-- Single shared update function reused for all three new tables - keeps the
-- schema DRY and matches the pattern already used by projects/drawings.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION update_materials_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_materials_timestamp ON public.materials;
CREATE TRIGGER trigger_materials_timestamp
    BEFORE UPDATE ON public.materials
    FOR EACH ROW EXECUTE FUNCTION update_materials_timestamp();

DROP TRIGGER IF EXISTS trigger_plates_timestamp ON public.material_plates;
CREATE TRIGGER trigger_plates_timestamp
    BEFORE UPDATE ON public.material_plates
    FOR EACH ROW EXECUTE FUNCTION update_materials_timestamp();

DROP TRIGGER IF EXISTS trigger_profiles_timestamp ON public.profiles;
CREATE TRIGGER trigger_profiles_timestamp
    BEFORE UPDATE ON public.profiles
    FOR EACH ROW EXECUTE FUNCTION update_materials_timestamp();

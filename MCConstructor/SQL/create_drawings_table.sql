-- ============================================================================
-- Migration: create_drawings_table
-- Tracks every .dwg file that belongs to a project together with its
-- "drawing properties" (type and discipline). The actual file lives on the
-- file system at file_path; this table is the searchable registry.
-- Run AFTER create_projects_table.sql.
-- ============================================================================

-- Drop existing table if you need to recreate (be careful in production!)
-- DROP TABLE IF EXISTS public.drawings;

CREATE TABLE IF NOT EXISTS public.drawings (
    -- Primary identification
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES public.projects(id) ON DELETE CASCADE,

    -- Drawing info
    name VARCHAR(255) NOT NULL,
    description TEXT,

    -- Drawing classification (mirrors the Drawing Properties dialog).
    -- Allowed values are validated by CHECK constraint below; keep them in
    -- sync with MCConstructor.DrawingTypes / DrawingDisciplines.
    drawing_type VARCHAR(50) NOT NULL,
    discipline VARCHAR(50),  -- NULL when the type doesn't have a discipline

    -- Absolute path to the .dwg file on the local file system.
    file_path TEXT NOT NULL,

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    created_by VARCHAR(255),
    updated_by VARCHAR(255),

    -- Constraints
    CONSTRAINT chk_drawings_drawing_type CHECK (drawing_type IN (
        'Base',
        'BlockLibrary',
        'FunctionalDrawing',
        'DetailDrawing',
        'Template',
        'Titleblock',
        'Sheet',
        'Profile',
        'ProfilePlot',
        'Other'
    )),
    CONSTRAINT chk_drawings_discipline CHECK (discipline IS NULL OR discipline IN (
        'General',
        'Piping',
        'Electrical',
        'HVAC'
    )),

    -- Drawing names should be unique within a project so the plugin can
    -- detect duplicates before writing to disk.
    CONSTRAINT uq_drawings_project_name UNIQUE (project_id, name)
);

-- Indexes for common queries
CREATE INDEX IF NOT EXISTS idx_drawings_project_id   ON public.drawings(project_id);
CREATE INDEX IF NOT EXISTS idx_drawings_drawing_type ON public.drawings(drawing_type);
CREATE INDEX IF NOT EXISTS idx_drawings_discipline   ON public.drawings(discipline);

-- Trigger to update the updated_at timestamp
CREATE OR REPLACE FUNCTION update_drawings_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_drawings_timestamp ON public.drawings;
CREATE TRIGGER trigger_drawings_timestamp
    BEFORE UPDATE ON public.drawings
    FOR EACH ROW
    EXECUTE FUNCTION update_drawings_timestamp();

-- ============================================================================
-- Migration: create_projects_table
-- Creates the projects table that all other tables (drawings, drawing_parts)
-- reference via project_id. Run this BEFORE create_drawings_table.sql or
-- create_drawing_parts_table.sql.
-- ============================================================================

-- Drop existing table if you need to recreate (be careful in production!)
-- DROP TABLE IF EXISTS public.projects CASCADE;

CREATE TABLE IF NOT EXISTS public.projects (
    -- Primary identification
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Project info
    name VARCHAR(255) NOT NULL UNIQUE,
    description TEXT,

    -- Local file system root for this project (e.g. C:\Projects\MyProject).
    -- The plugin creates a standard folder structure under this path.
    directory TEXT NOT NULL,

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    created_by VARCHAR(255),
    updated_by VARCHAR(255)
);

-- For older databases that already have a `projects` table without a
-- `directory` column, add it without losing existing data.
ALTER TABLE public.projects
    ADD COLUMN IF NOT EXISTS directory TEXT;

-- Indexes for common queries
CREATE INDEX IF NOT EXISTS idx_projects_name ON public.projects(name);

-- Trigger to update the updated_at timestamp
CREATE OR REPLACE FUNCTION update_projects_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_projects_timestamp ON public.projects;
CREATE TRIGGER trigger_projects_timestamp
    BEFORE UPDATE ON public.projects
    FOR EACH ROW
    EXECUTE FUNCTION update_projects_timestamp();

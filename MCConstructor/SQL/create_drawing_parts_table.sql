-- Drop existing table if you need to recreate (be careful in production!)
-- DROP TABLE IF EXISTS public.drawing_parts;

-- Create the drawing_parts table to store complete part data for recreation
CREATE TABLE IF NOT EXISTS public.drawing_parts (
    -- Primary identification
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES public.projects(id) ON DELETE CASCADE,

    -- Part identification
    part_name VARCHAR(255) NOT NULL,
    part_description TEXT,

    -- Source tracking (where this part was originally created)
    source_drawing_name VARCHAR(255) NOT NULL,
    source_object_handle VARCHAR(50) NOT NULL,

    -- Reference tracking (for inserted parts)
    parent_part_id UUID REFERENCES public.drawing_parts(id) ON DELETE SET NULL,
    is_original BOOLEAN DEFAULT TRUE,  -- TRUE if this is the master definition
    is_override BOOLEAN DEFAULT FALSE, -- TRUE if this instance has local changes that should sync back

    -- Geometry data (ACIS/SAT format for 3D solids)
    geometry_data TEXT,  -- SAT format string for the 3D solid
    geometry_type VARCHAR(50) DEFAULT '3DSOLID',  -- Type of geometry (3DSOLID, etc.)

    -- Transform data (location, rotation, scale)
    position_x DOUBLE PRECISION DEFAULT 0,
    position_y DOUBLE PRECISION DEFAULT 0,
    position_z DOUBLE PRECISION DEFAULT 0,

    -- Rotation (in radians)
    rotation_x DOUBLE PRECISION DEFAULT 0,
    rotation_y DOUBLE PRECISION DEFAULT 0,
    rotation_z DOUBLE PRECISION DEFAULT 0,

    -- Scale
    scale_x DOUBLE PRECISION DEFAULT 1,
    scale_y DOUBLE PRECISION DEFAULT 1,
    scale_z DOUBLE PRECISION DEFAULT 1,

    -- Visual properties
    layer_name VARCHAR(255) DEFAULT '0',
    color_index INTEGER DEFAULT 256,  -- 256 = ByLayer
    material_name VARCHAR(255),

    -- Bounding box (for quick spatial queries)
    bbox_min_x DOUBLE PRECISION,
    bbox_min_y DOUBLE PRECISION,
    bbox_min_z DOUBLE PRECISION,
    bbox_max_x DOUBLE PRECISION,
    bbox_max_y DOUBLE PRECISION,
    bbox_max_z DOUBLE PRECISION,

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    created_by VARCHAR(255),
    updated_by VARCHAR(255),

    -- Version tracking for sync
    version INTEGER DEFAULT 1
);

-- Indexes for common queries
CREATE INDEX IF NOT EXISTS idx_drawing_parts_project_id ON public.drawing_parts(project_id);
CREATE INDEX IF NOT EXISTS idx_drawing_parts_part_name ON public.drawing_parts(part_name);
CREATE INDEX IF NOT EXISTS idx_drawing_parts_source_drawing ON public.drawing_parts(source_drawing_name);
CREATE INDEX IF NOT EXISTS idx_drawing_parts_parent_part ON public.drawing_parts(parent_part_id);
CREATE INDEX IF NOT EXISTS idx_drawing_parts_is_original ON public.drawing_parts(is_original);

-- Trigger to update the updated_at timestamp
CREATE OR REPLACE FUNCTION update_drawing_parts_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    NEW.version = OLD.version + 1;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_drawing_parts_timestamp ON public.drawing_parts;
CREATE TRIGGER trigger_drawing_parts_timestamp
    BEFORE UPDATE ON public.drawing_parts
    FOR EACH ROW
    EXECUTE FUNCTION update_drawing_parts_timestamp();

-- ============================================================================
-- USEFUL QUERIES FOR YOUR REACT APP
-- ============================================================================

-- Get all original (master) parts for a project
-- SELECT * FROM public.drawing_parts WHERE project_id = 'uuid' AND is_original = TRUE;

-- Get all instances of a specific part (including references)
-- SELECT * FROM public.drawing_parts WHERE parent_part_id = 'part-uuid' OR id = 'part-uuid';

-- Get parts that have been overridden and need sync
-- SELECT * FROM public.drawing_parts WHERE is_override = TRUE;

-- Get part count by drawing
-- SELECT source_drawing_name, COUNT(*) as part_count
-- FROM public.drawing_parts
-- WHERE project_id = 'uuid' AND is_original = TRUE
-- GROUP BY source_drawing_name;

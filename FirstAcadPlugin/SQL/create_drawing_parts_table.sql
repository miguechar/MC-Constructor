-- Create the drawing_parts table to store part metadata from AutoCAD
-- This table links parts to projects via projectId

CREATE TABLE IF NOT EXISTS public.drawing_parts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES public.projects(id) ON DELETE CASCADE,
    drawing_name VARCHAR(255) NOT NULL,
    part_name VARCHAR(255) NOT NULL,
    object_handle VARCHAR(50),  -- AutoCAD object handle for reference
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),

    -- Add any additional metadata fields here as needed
    -- For example:
    -- material VARCHAR(100),
    -- part_number VARCHAR(100),
    -- quantity INTEGER DEFAULT 1,

    CONSTRAINT fk_project
        FOREIGN KEY (project_id)
        REFERENCES public.projects(id)
        ON DELETE CASCADE
);

-- Create an index on project_id for faster queries
CREATE INDEX IF NOT EXISTS idx_drawing_parts_project_id ON public.drawing_parts(project_id);

-- Create an index on drawing_name for filtering by drawing
CREATE INDEX IF NOT EXISTS idx_drawing_parts_drawing_name ON public.drawing_parts(drawing_name);

-- Create a trigger to automatically update the updated_at timestamp
CREATE OR REPLACE FUNCTION update_drawing_parts_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_update_drawing_parts_updated_at ON public.drawing_parts;

CREATE TRIGGER trigger_update_drawing_parts_updated_at
    BEFORE UPDATE ON public.drawing_parts
    FOR EACH ROW
    EXECUTE FUNCTION update_drawing_parts_updated_at();

-- Example query to get all parts for a project (for your React app):
-- SELECT * FROM public.drawing_parts WHERE project_id = 'your-project-uuid' ORDER BY drawing_name, part_name;

-- Example query to get parts grouped by drawing:
-- SELECT drawing_name, COUNT(*) as part_count, array_agg(part_name) as parts
-- FROM public.drawing_parts
-- WHERE project_id = 'your-project-uuid'
-- GROUP BY drawing_name;

-- Add material_id to drawing_parts so each part can reference a material
-- from the project's material library.
ALTER TABLE public.drawing_parts
    ADD COLUMN IF NOT EXISTS material_id UUID REFERENCES public.materials(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_drawing_parts_material_id ON public.drawing_parts(material_id);

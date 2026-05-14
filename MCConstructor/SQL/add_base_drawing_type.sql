-- ============================================================================
-- Migration: add_base_drawing_type
-- Extends the drawing_type CHECK constraint to include 'Base' (background /
-- template drawing used as the starting content for other drawings).
-- Safe to run against existing databases.
-- ============================================================================

ALTER TABLE public.drawings
    DROP CONSTRAINT IF EXISTS chk_drawings_drawing_type;

ALTER TABLE public.drawings
    ADD CONSTRAINT chk_drawings_drawing_type CHECK (drawing_type IN (
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
    ));

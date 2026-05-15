-- ============================================================================
-- Migration: add_profile_drawing_types
-- Extends the drawing_type CHECK constraint to include 'Profile' and
-- 'ProfilePlot' (cross-section drawings and generated profile-plot sheets).
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

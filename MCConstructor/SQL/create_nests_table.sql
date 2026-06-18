-- Tracks nesting runs for a project: plate, material, export status, and cut tracking.
-- Run once against the target PostgreSQL database.

CREATE TABLE IF NOT EXISTS public.nests (
    id               UUID             PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id       UUID             NOT NULL REFERENCES public.projects(id) ON DELETE CASCADE,
    drawing_id       UUID             REFERENCES public.drawings(id) ON DELETE SET NULL,
    name             TEXT             NOT NULL,
    created_at       TIMESTAMPTZ      NOT NULL DEFAULT NOW(),
    material_name    TEXT,
    plate_code       TEXT,
    plate_dimensions TEXT,
    part_count       INTEGER          NOT NULL DEFAULT 0,
    efficiency       DOUBLE PRECISION NOT NULL DEFAULT 0,
    sent_to_cut      BOOLEAN          NOT NULL DEFAULT FALSE,
    cut              BOOLEAN          NOT NULL DEFAULT FALSE,
    cut_date         DATE,
    nest_location    TEXT,
    dwg_path         TEXT
);

CREATE INDEX IF NOT EXISTS nests_project_id_idx ON public.nests (project_id);

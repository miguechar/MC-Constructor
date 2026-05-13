using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.IO;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Generates a profile-plot process sheet (.dwg) containing three scaled views
    /// (front elevation, plan view, end/cross-section view) with true-value dimensions.
    /// Geometry is scaled to fit in a 2000 × 1500 mm sheet; dimension text always
    /// shows the real model value regardless of scale.
    /// </summary>
    public static class ProfilePlotService
    {
        // ── Sheet layout (all values in mm, matching model units) ────────────
        const double ShW = 2000, ShH = 1500;
        const double TitleY = 1330;            // bottom of title strip

        // Front elevation  (length × csH)
        const double FX0 = 80,   FX1 = 1460;  // 1380 mm wide
        const double FY0 = 770,  FY1 = 1300;  // 530 mm tall

        // Plan view  (length × csW)
        const double PX0 = 80,   PX1 = 1460;
        const double PY0 = 80,   PY1 = 700;   // 620 mm tall

        // End view  (csW × csH)
        const double EX0 = 1520, EX1 = 1970;  // 450 mm wide
        const double EY0 = 80,   EY1 = 1300;  // 1220 mm tall

        // Dimension clearance from view rect edge to dim line
        const double DimOff  = 80;
        const double EDimOff = 60;  // tighter offset for the end view

        // Text heights (mm)
        const double TitleH = 55;
        const double SubH   = 38;
        const double LabelH = 42;
        const double DimTxt = 25;

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the profile-plot drawing and saves it to <paramref name="outputPath"/>.
        /// </summary>
        /// <param name="profileDrawingPath">Absolute path to the Profile (.dwg) cross-section drawing.</param>
        /// <param name="length">True profile length in drawing units (mm).</param>
        /// <param name="outputPath">Where to save the generated plot .dwg.</param>
        /// <param name="profileName">Display name used in the title strip.</param>
        /// <param name="templatePath">
        /// Optional path to PROFILE_PLOTS_TEMPLATE.dwg.  When supplied the template is
        /// copied to <paramref name="outputPath"/> before writing, so its linetypes,
        /// layers and dim styles are preserved.  Dim-property overrides are skipped when
        /// a template is used — style the dimensions in the template file instead.
        /// </param>
        public static void CreatePlot(
            string profileDrawingPath,
            double length,
            string outputPath,
            string profileName,
            string templatePath = null)
        {
            if (length <= 0) throw new ArgumentException("Length must be > 0.");

            // ── 1. Read cross-section info from the profile drawing ───────────
            double csW = 0, csH = 0;
            double csMinX = 0, csMinY = 0;
            var csSourceIds = new ObjectIdCollection();

            using (var sideDb = new Database(false, true))
            {
                sideDb.ReadDwgFile(profileDrawingPath, FileShare.ReadWrite, true, "");
                sideDb.CloseInput(true);

                using (var tr = sideDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    bool hasGeom = false;

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        csSourceIds.Add(id);
                        try
                        {
                            var e = ent.GeometricExtents;
                            minX = Math.Min(minX, e.MinPoint.X);
                            minY = Math.Min(minY, e.MinPoint.Y);
                            maxX = Math.Max(maxX, e.MaxPoint.X);
                            maxY = Math.Max(maxY, e.MaxPoint.Y);
                            hasGeom = true;
                        }
                        catch { }
                    }
                    tr.Commit();

                    if (!hasGeom)
                        throw new InvalidOperationException(
                            "No geometry found in the profile drawing.");

                    csW    = maxX - minX;
                    csH    = maxY - minY;
                    csMinX = minX;
                    csMinY = minY;
                }

                if (csW < 1e-6 || csH < 1e-6)
                    throw new InvalidOperationException(
                        "Cross-section bounding box is zero — check profile drawing geometry.");

                // ── 2. Compute scale factors ──────────────────────────────────
                // Leave DimOff space around each view for dim lines.
                double fAvailW = (FX1 - FX0) - DimOff - 20;   // 20mm right margin
                double fAvailH = (FY1 - FY0) - DimOff - 20;
                double pAvailH = (PY1 - PY0) - DimOff - 20;
                double eAvailW = (EX1 - EX0) - EDimOff - 20;
                double eAvailH = (EY1 - EY0) - EDimOff - 20;

                double mainScale = Math.Min(fAvailW / length,
                                   Math.Min(fAvailH / csH, pAvailH / csW)) * 0.88;
                mainScale = Math.Max(mainScale, 1e-9);

                double endScale  = Math.Min(eAvailW / csW, eAvailH / csH) * 0.88;
                endScale = Math.Max(endScale, 1e-9);

                // ── 3. Build the output drawing ───────────────────────────────
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                bool usingTemplate = !string.IsNullOrEmpty(templatePath) && File.Exists(templatePath);
                if (usingTemplate)
                    File.Copy(templatePath, outputPath, overwrite: true);

                using (var db = usingTemplate
                    ? OpenTemplateDb(outputPath)
                    : new Database(true, true))
                {
                    // Grab the dim-style ObjectId from this database BEFORE any transaction.
                    // Never pass ObjectId.Null — AutoCAD resolves Null from the active document
                    // and throws eWrongDatabase in a side-database context.
                    ObjectId dimStyleId = db.Dimstyle;

                    // When no template is present, apply fallback dim appearance.
                    // When a template IS present the user styles dimensions in the template itself.
                    if (!usingTemplate)
                    {
                        db.Dimtxt = DimTxt;
                        db.Dimasz = 25;
                        db.Dimexo = 12;
                        db.Dimexe = 20;
                        db.Dimgap = 12;
                        db.Dimdec = 0;
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(
                            bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        // Sheet border
                        AddRect(ms, tr, 10, 10, ShW - 20, ShH - 20);

                        // Title strip
                        AddLine(ms, tr, 10, TitleY, ShW - 10, TitleY);
                        int scaleRatio = Math.Max(1, (int)Math.Round(1.0 / mainScale));
                        AddCText(ms, tr, ShW / 2, TitleY + (ShH - TitleY) * 0.65,
                            $"PROFILE PLOT  —  {profileName.ToUpper()}", TitleH);
                        AddCText(ms, tr, ShW / 2, TitleY + (ShH - TitleY) * 0.28,
                            $"LENGTH: {length:F0} mm     SCALE 1:{scaleRatio}", SubH);

                        // Divider between front and plan views
                        double midY = (FY0 + PY1) / 2.0;
                        AddLine(ms, tr, 10, midY, EX0, midY);
                        // Divider between main block and end view column
                        AddLine(ms, tr, EX0, PY0 - 20, EX0, TitleY);

                        // ── Front Elevation ───────────────────────────────────
                        double fLen = length * mainScale;
                        double fH   = csH    * mainScale;
                        double fX = (FX0 + DimOff) + ((FX1 - FX0 - DimOff - 20) - fLen) / 2;
                        double fY = FY0 + DimOff / 2 + (fAvailH - fH) / 2;

                        AddRect(ms, tr, fX, fY, fLen, fH);
                        AddCentreline(ms, tr, fX - 10, fY + fH / 2, fX + fLen + 10, fY + fH / 2);
                        AddCText(ms, tr, fX + fLen / 2, FY1 - LabelH * 0.7, "FRONT ELEVATION", LabelH);

                        AddHDim(ms, tr, fX, fX + fLen, fY, fY - DimOff, $"{length:F0}", dimStyleId);
                        AddVDim(ms, tr, fY, fY + fH, fX, fX - DimOff, $"{csH:F0}", dimStyleId);

                        // ── Plan View ─────────────────────────────────────────
                        double pLen = length * mainScale;
                        double pW   = csW    * mainScale;
                        double pX   = fX;
                        double pY   = PY0 + DimOff / 2 + (pAvailH - pW) / 2;

                        AddRect(ms, tr, pX, pY, pLen, pW);
                        AddCentreline(ms, tr, pX - 10, pY + pW / 2, pX + pLen + 10, pY + pW / 2);
                        AddCText(ms, tr, pX + pLen / 2, PY1 - LabelH * 0.7, "PLAN VIEW", LabelH);

                        AddHDim(ms, tr, pX, pX + pLen, pY, pY - DimOff, $"{length:F0}", dimStyleId);
                        AddVDim(ms, tr, pY, pY + pW, pX, pX - DimOff, $"{csW:F0}", dimStyleId);

                        // ── End View layout (geometry cloned after this tr) ───
                        double eW = csW * endScale;
                        double eH = csH * endScale;
                        double eX = EX0 + EDimOff + (eAvailW - eW) / 2;
                        double eY = EY0 + EDimOff / 2 + (eAvailH - eH) / 2;

                        AddCText(ms, tr, (EX0 + EX1) / 2, EY1 - LabelH * 0.7, "END VIEW", LabelH);
                        AddHDim(ms, tr, eX, eX + eW, eY, eY - EDimOff, $"{csW:F0}", dimStyleId);
                        AddVDim(ms, tr, eY, eY + eH, eX, eX - EDimOff, $"{csH:F0}", dimStyleId);

                        tr.Commit();
                    }

                    // ── 4. WblockClone cross-section geometry into end view ───
                    if (csSourceIds.Count > 0)
                    {
                        double eW = csW * endScale;
                        double eH = csH * endScale;
                        double eX = EX0 + EDimOff + (eAvailW - eW) / 2;
                        double eY = EY0 + EDimOff / 2 + (eAvailH - eH) / 2;

                        // Snapshot model space before clone
                        ObjectId targetMsId;
                        var preExisting = new HashSet<ObjectId>();
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            targetMsId = bt[BlockTableRecord.ModelSpace];
                            var ms = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForRead);
                            foreach (ObjectId id in ms) preExisting.Add(id);
                            tr.Commit();
                        }

                        sideDb.WblockCloneObjects(
                            csSourceIds, targetMsId, new IdMapping(),
                            DuplicateRecordCloning.Replace, false);

                        // Scale + translate cloned entities into the end view area.
                        // Cross-section origin in profile drawing may not be at world (0,0);
                        // compensate with csMinX / csMinY so the shape's lower-left lands at (eX, eY).
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            var ms = (BlockTableRecord)tr.GetObject(
                                bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                            var xform = Matrix3d.Displacement(
                                            new Vector3d(eX - csMinX * endScale,
                                                         eY - csMinY * endScale, 0))
                                        * Matrix3d.Scaling(endScale, Point3d.Origin);

                            foreach (ObjectId id in ms)
                            {
                                if (preExisting.Contains(id)) continue;
                                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                                ent?.TransformBy(xform);
                            }
                            tr.Commit();
                        }
                    }

                    db.SaveAs(outputPath, DwgVersion.Current);
                }
            }
        }

        // Opens an existing .dwg as a writable side-database.
        private static Database OpenTemplateDb(string path)
        {
            var db = new Database(false, true);
            db.ReadDwgFile(path, FileShare.ReadWrite, true, "");
            db.CloseInput(true);
            return db;
        }

        // ── Drawing helpers ───────────────────────────────────────────────────

        static void AddRect(BlockTableRecord ms, Transaction tr,
            double x, double y, double w, double h)
        {
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(x,     y),     0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x + w, y),     0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x + w, y + h), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x,     y + h), 0, 0, 0);
            pl.Closed = true;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        static void AddLine(BlockTableRecord ms, Transaction tr,
            double x0, double y0, double x1, double y1)
        {
            var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        static void AddCentreline(BlockTableRecord ms, Transaction tr,
            double x0, double y, double x1, double y1)
        {
            var ln = new Line(new Point3d(x0, y, 0), new Point3d(x1, y1, 0));
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        static void AddCText(BlockTableRecord ms, Transaction tr,
            double cx, double cy, string text, double height)
        {
            var t = new DBText
            {
                TextString     = text,
                Height         = height,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode   = TextVerticalMode.TextVerticalMid,
                Position       = new Point3d(cx, cy, 0),
                AlignmentPoint = new Point3d(cx, cy, 0)
            };
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        /// <summary>
        /// Horizontal aligned dimension. <paramref name="dimStyleId"/> must come from
        /// the same database as <paramref name="ms"/> — never pass ObjectId.Null, which
        /// causes AutoCAD to resolve the style from the active document and throws eWrongDatabase.
        /// </summary>
        static void AddHDim(BlockTableRecord ms, Transaction tr,
            double x0, double x1, double objY, double dimY, string trueText, ObjectId dimStyleId)
        {
            var dim = new AlignedDimension(
                new Point3d(x0, objY, 0),
                new Point3d(x1, objY, 0),
                new Point3d((x0 + x1) / 2, dimY, 0),
                trueText,
                dimStyleId);
            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        /// <summary>
        /// Vertical aligned dimension. See <see cref="AddHDim"/> for dimStyleId note.
        /// </summary>
        static void AddVDim(BlockTableRecord ms, Transaction tr,
            double y0, double y1, double objX, double dimX, string trueText, ObjectId dimStyleId)
        {
            var dim = new AlignedDimension(
                new Point3d(objX, y0, 0),
                new Point3d(objX, y1, 0),
                new Point3d(dimX, (y0 + y1) / 2, 0),
                trueText,
                dimStyleId);
            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }
    }
}

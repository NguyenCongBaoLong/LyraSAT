-- ============================================================
-- LyraSAT - Kiem tra & don dep du lieu ma lop khong nhat quan
-- Postgres (Supabase). Chay bang SQL Editor hoac psql.
--
-- Nguyen tac:
--   * BUOC 1-3 chi DOC, khong thay doi du lieu.
--   * BUOC 4 sua drift hoa/thuong va khoang trang (an toan, xac dinh duoc).
--   * BUOC 5-6 la thao tac XOA/SUA can nguoi xac nhan, mac dinh comment out.
--   * Script idempotent: chay lai nhieu lan cho ket qua nhu nhau.
-- ============================================================

-- ------------------------------------------------------------
-- BUOC 1. Ma lop mo coi: co du lieu nhung khong co trong "Classes"
-- ------------------------------------------------------------

-- 1.1 De thi tro toi ma lop khong ton tai
SELECT t."ClassCode", count(*) AS tests, min(t."Id") AS min_test_id, max(t."Id") AS max_test_id
FROM "Tests" t
LEFT JOIN "Classes" c
       ON lower(trim(c."ClassCode")) = lower(trim(coalesce(t."ClassCode", '')))
WHERE coalesce(t."ClassCode", '') <> ''
  AND c."Id" IS NULL
GROUP BY t."ClassCode"
ORDER BY tests DESC;

-- 1.2 Tu vung tro toi ma lop khong ton tai
SELECT v."ClassCode", count(*) AS words
FROM "Vocabularies" v
LEFT JOIN "Classes" c
       ON lower(trim(c."ClassCode")) = lower(trim(coalesce(v."ClassCode", '')))
WHERE v."ClassCode" IS NOT NULL
  AND c."Id" IS NULL
GROUP BY v."ClassCode"
ORDER BY words DESC;

-- 1.3 Phan cong giao vien tro toi ma lop khong ton tai
--     (day chinh la loai row sot lai khi xoa lop, vi ClassTeachers khong co FK toi Classes)
SELECT ct."ClassCode", count(*) AS rows, array_agg(ct."TeacherId" ORDER BY ct."TeacherId") AS teacher_ids
FROM "ClassTeachers" ct
LEFT JOIN "Classes" c
       ON lower(trim(c."ClassCode")) = lower(trim(ct."ClassCode"))
WHERE c."Id" IS NULL
GROUP BY ct."ClassCode"
ORDER BY rows DESC;

-- 1.4 Hoc sinh mang ma lop khong ton tai
SELECT u."ClassCode", count(*) AS users
FROM "Users" u
LEFT JOIN "Classes" c
       ON lower(trim(c."ClassCode")) = lower(trim(coalesce(u."ClassCode", '')))
WHERE coalesce(u."ClassCode", '') <> ''
  AND u."ClassCode" <> 'SYSTEM'
  AND c."Id" IS NULL
GROUP BY u."ClassCode"
ORDER BY users DESC;

-- ------------------------------------------------------------
-- BUOC 2. Drift hoa/thuong & khoang trang: cung mot lop, khac chuoi
-- ------------------------------------------------------------
SELECT 'Tests' AS src, t."ClassCode" AS code, c."ClassCode" AS canonical, count(*) AS n
FROM "Tests" t
JOIN "Classes" c ON lower(trim(t."ClassCode")) = lower(trim(c."ClassCode"))
WHERE t."ClassCode" <> c."ClassCode"
GROUP BY 1, 2, 3
UNION ALL
SELECT 'Vocabularies', v."ClassCode", c."ClassCode", count(*)
FROM "Vocabularies" v
JOIN "Classes" c ON lower(trim(v."ClassCode")) = lower(trim(c."ClassCode"))
WHERE v."ClassCode" <> c."ClassCode"
GROUP BY 1, 2, 3
UNION ALL
SELECT 'ClassTeachers', ct."ClassCode", c."ClassCode", count(*)
FROM "ClassTeachers" ct
JOIN "Classes" c ON lower(trim(ct."ClassCode")) = lower(trim(c."ClassCode"))
WHERE ct."ClassCode" <> c."ClassCode"
GROUP BY 1, 2, 3
UNION ALL
SELECT 'Users', u."ClassCode", c."ClassCode", count(*)
FROM "Users" u
JOIN "Classes" c ON lower(trim(u."ClassCode")) = lower(trim(c."ClassCode"))
WHERE u."ClassCode" <> c."ClassCode"
GROUP BY 1, 2, 3;

-- ------------------------------------------------------------
-- BUOC 3. Thong bao mo coi: Notification khong co FK toi Tests/Questions/Comments
-- ------------------------------------------------------------
SELECT n."Type", count(*) AS orphan_notifications
FROM "Notifications" n
WHERE (n."TestId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Tests" t WHERE t."Id" = n."TestId"))
   OR (n."QuestionId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Questions" q WHERE q."Id" = n."QuestionId"))
   OR (n."CommentId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "QuestionComments" qc WHERE qc."Id" = n."CommentId"))
GROUP BY n."Type";

-- ============================================================
-- BUOC 4. SUA DRIFT (an toan): dua cac chuoi chi khac hoa/thuong
-- hoac khoang trang ve dung ma lop trong bang "Classes".
-- ============================================================

BEGIN;

UPDATE "Tests" t
SET "ClassCode" = c."ClassCode"
FROM "Classes" c
WHERE lower(trim(t."ClassCode")) = lower(trim(c."ClassCode"))
  AND t."ClassCode" <> c."ClassCode";

UPDATE "Vocabularies" v
SET "ClassCode" = c."ClassCode"
FROM "Classes" c
WHERE v."ClassCode" IS NOT NULL
  AND lower(trim(v."ClassCode")) = lower(trim(c."ClassCode"))
  AND v."ClassCode" <> c."ClassCode";

UPDATE "ClassTeachers" ct
SET "ClassCode" = c."ClassCode"
FROM "Classes" c
WHERE lower(trim(ct."ClassCode")) = lower(trim(c."ClassCode"))
  AND ct."ClassCode" <> c."ClassCode";

UPDATE "Users" u
SET "ClassCode" = c."ClassCode"
FROM "Classes" c
WHERE u."ClassCode" IS NOT NULL
  AND lower(trim(u."ClassCode")) = lower(trim(c."ClassCode"))
  AND u."ClassCode" <> c."ClassCode";

COMMIT;

-- ============================================================
-- BUOC 5. MA LOP MO COI CAN NGUOI QUYET DINH
-- Script khong tu doan: ma khong khop voi lop nao thi phai xem tay,
-- vi co the la ma lop go sai (vi du "f2609" trong khi lop that la "SF2609").
-- ============================================================

-- 5.1 Vi du: dua toan bo de thi cua ma go sai ve lop dung.
--     Chi chay khi da xac nhan day la cung mot lop.
--
-- BEGIN;
-- UPDATE "Tests"          SET "ClassCode" = 'SF2609' WHERE lower(trim("ClassCode")) = 'f2609';
-- UPDATE "Vocabularies"   SET "ClassCode" = 'SF2609' WHERE lower(trim("ClassCode")) = 'f2609';
-- UPDATE "Users"          SET "ClassCode" = 'SF2609' WHERE lower(trim("ClassCode")) = 'f2609';
-- UPDATE "ClassTeachers"  SET "ClassCode" = 'SF2609'
--   WHERE lower(trim("ClassCode")) = 'f2609'
--     AND NOT EXISTS (
--       SELECT 1 FROM "ClassTeachers" ct2
--       WHERE lower(trim(ct2."ClassCode")) = 'sf2609' AND ct2."TeacherId" = "ClassTeachers"."TeacherId");
-- DELETE FROM "ClassTeachers" WHERE lower(trim("ClassCode")) = 'f2609';
-- COMMIT;
--
-- 5.2 Hoac xoa han phan cong giao vien mo coi (khong con lop, khong con de thi):
--
-- DELETE FROM "ClassTeachers" ct
-- WHERE NOT EXISTS (SELECT 1 FROM "Classes" c WHERE lower(trim(c."ClassCode")) = lower(trim(ct."ClassCode")))
--   AND NOT EXISTS (SELECT 1 FROM "Tests" t WHERE lower(trim(t."ClassCode")) = lower(trim(ct."ClassCode")));

-- ============================================================
-- BUOC 6. DON THONG BAO MO COI (xoa du lieu - can xac nhan)
-- ============================================================
--
-- DELETE FROM "Notifications" n
-- WHERE (n."TestId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Tests" t WHERE t."Id" = n."TestId"))
--    OR (n."QuestionId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Questions" q WHERE q."Id" = n."QuestionId"))
--    OR (n."CommentId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "QuestionComments" qc WHERE qc."Id" = n."CommentId"));

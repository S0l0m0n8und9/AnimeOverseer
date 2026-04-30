CREATE TABLE "Themes" (
    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL
);

CREATE TABLE "AnimeThemes" (
    "AnimeId" INTEGER NOT NULL,
    "ThemeId" INTEGER NOT NULL,
    CONSTRAINT "PK_AnimeThemes" PRIMARY KEY ("AnimeId", "ThemeId"),
    CONSTRAINT "FK_AnimeThemes_Animes_AnimeId" FOREIGN KEY ("AnimeId") REFERENCES "Animes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AnimeThemes_Themes_ThemeId" FOREIGN KEY ("ThemeId") REFERENCES "Themes" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Demographics" (
    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL
);

CREATE TABLE "AnimeDemographics" (
    "AnimeId" INTEGER NOT NULL,
    "DemographicId" INTEGER NOT NULL,
    CONSTRAINT "PK_AnimeDemographics" PRIMARY KEY ("AnimeId", "DemographicId"),
    CONSTRAINT "FK_AnimeDemographics_Animes_AnimeId" FOREIGN KEY ("AnimeId") REFERENCES "Animes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AnimeDemographics_Demographics_DemographicId" FOREIGN KEY ("DemographicId") REFERENCES "Demographics" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_AnimeThemes_ThemeId" ON "AnimeThemes" ("ThemeId");

CREATE INDEX "IX_AnimeDemographics_DemographicId" ON "AnimeDemographics" ("DemographicId");

using Microsoft.EntityFrameworkCore.Migrations;

namespace ServeX.Migrations
{
    public partial class StandardTableLabels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH Legacy AS (
                    SELECT Id,
                        CASE
                            WHEN Area = 'Indoor' AND TableNumber GLOB 'IN-*' THEN substr(TableNumber, 4)
                            WHEN Area = 'Indoor' AND TableNumber GLOB 'I-*' THEN substr(TableNumber, 3)
                            WHEN Area = 'Outdoor' AND TableNumber GLOB 'OUT-*' THEN substr(TableNumber, 5)
                            WHEN Area = 'Outdoor' AND TableNumber GLOB 'O-*' THEN substr(TableNumber, 3)
                        END AS Digits,
                        CASE Area WHEN 'Indoor' THEN 'T' WHEN 'Outdoor' THEN 'OT' END AS Prefix
                    FROM Tables
                ), Candidates AS MATERIALIZED (
                    SELECT MIN(Id) AS Id, Prefix || Digits AS NewLabel
                    FROM Legacy
                    WHERE Digits GLOB '[0-9]*' AND Digits NOT GLOB '*[^0-9]*'
                      AND (Digits = '0' OR Digits NOT GLOB '0*')
                    GROUP BY Prefix, Digits
                ), Renames AS MATERIALIZED (
                    SELECT Id, NewLabel
                    FROM Candidates
                    WHERE NOT EXISTS (
                        SELECT 1 FROM Tables WHERE TableNumber = NewLabel COLLATE NOCASE
                    )
                )
                UPDATE Tables
                SET TableNumber = (SELECT NewLabel FROM Renames WHERE Renames.Id = Tables.Id)
                WHERE Id IN (SELECT Id FROM Renames);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Original spellings cannot be recovered from the new labels alone.
        }
    }
}

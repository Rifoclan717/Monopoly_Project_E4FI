using System.Collections.Generic;

namespace CampusTycoon.AI.Simulation
{
    /// <summary>Plateau de référence (niveau 2), recopié du document de règles.</summary>
    public static class Boards
    {
        private static int[] R(int b, int m, int l1, int l2, int l3) => new[] { b, m, l1, l2, l3 };

        public static List<SquareInfo> Level2() => new List<SquareInfo>
        {
            new SquareInfo("La Rentrée", SquareType.Start, amount: 150),                                   // 0
            new SquareInfo("Cafétéria", SquareType.Property, ColorGroup.Brown, 60, R(6, 12, 30, 72, 132), 50),   // 1
            new SquareInfo("Salle de TP", SquareType.Property, ColorGroup.Brown, 60, R(6, 12, 30, 72, 132), 50), // 2
            new SquareInfo("Événement", SquareType.EventCard),                                              // 3
            new SquareInfo("RER A", SquareType.Station, price: 150, rents: new[] { 25, 60 }),              // 4
            new SquareInfo("Colle", SquareType.Detention),                                                  // 5
            new SquareInfo("Bibliothèque", SquareType.Property, ColorGroup.LightBlue, 100, R(10, 20, 50, 120, 220), 50),     // 6
            new SquareInfo("Frais de scolarité", SquareType.Tax, amount: 100),                              // 7
            new SquareInfo("Salle de projet", SquareType.Property, ColorGroup.LightBlue, 100, R(10, 20, 50, 120, 220), 50),  // 8
            new SquareInfo("Machine à café", SquareType.Utility, price: 120, rents: new[] { 5 }),          // 9
            new SquareInfo("Foyer étudiant", SquareType.StudentLounge, amount: 50),                         // 10
            new SquareInfo("FabLab", SquareType.Property, ColorGroup.Pink, 140, R(14, 28, 70, 168, 308), 100),               // 11
            new SquareInfo("Événement", SquareType.EventCard),                                              // 12
            new SquareInfo("Salle de robotique", SquareType.Property, ColorGroup.Pink, 140, R(14, 28, 70, 168, 308), 100),   // 13
            new SquareInfo("Bus du campus", SquareType.Station, price: 150, rents: new[] { 25, 60 }),      // 14
            new SquareInfo("Rattrapage", SquareType.GoToDetention),                                         // 15
            new SquareInfo("Amphi A", SquareType.Property, ColorGroup.Orange, 180, R(18, 36, 90, 216, 396), 100),            // 16
            new SquareInfo("Amphi B", SquareType.Property, ColorGroup.Orange, 180, R(18, 36, 90, 216, 396), 100),            // 17
            new SquareInfo("Bureau du BDE", SquareType.Property, ColorGroup.Red, 240, R(24, 48, 120, 288, 528), 150),        // 18
            new SquareInfo("Direction", SquareType.Property, ColorGroup.Red, 260, R(26, 52, 130, 312, 572), 150),            // 19
        };
    }
}

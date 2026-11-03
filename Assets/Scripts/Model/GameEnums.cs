namespace CampusTycoon
{
    /// <summary>Type d'une case du plateau.</summary>
    public enum SquareType
    {
        Start,          // La Rentrée : bonus au passage
        Property,       // Terrain achetable et améliorable
        Station,        // Gare : RER A, Bus du campus
        Utility,        // Compagnie : Machine à café
        Tax,            // Frais de scolarité
        EventCard,      // Pioche une carte Événement
        Detention,      // Colle (simple visite si on s'y arrête)
        GoToDetention,  // Rattrapage : envoie en colle
        StudentLounge,  // Foyer étudiant : +50
        Roulette        // Roulette du BDE (niveau 3)
    }

    public enum ColorGroup
    {
        None,
        Brown,      // Cafétéria, Salle de TP
        LightBlue,  // Bibliothèque, Salle de projet
        Pink,       // FabLab, Salle de robotique
        Orange,     // Amphi A, Amphi B
        Red,        // Bureau du BDE, Direction
        Gold        // Incubateur, Laboratoire de recherche (niveau 3)
    }

    /// <summary>Effet d'une carte Événement.</summary>
    public enum CardEffect
    {
        GainMoney,              // Recevoir "amount"
        PayMoney,               // Payer "amount" à la banque
        MoveSteps,              // Avancer (amount > 0) ou reculer (amount < 0)
        MoveToStart,            // Aller à La Rentrée et toucher le bonus
        GoToDetention,          // Aller en colle
        CollectFromEachRival,   // Chaque rival donne "amount"
        SkipNextTurn,           // Passer son prochain tour
        RerollAndMove,          // Relancer les dés et rejouer un déplacement
        FreeUpgrade,            // +1 étage gratuit en monopole, sinon recevoir "amount"
        ExemptionTicket         // Carte gardée : sortir de colle ou annuler un loyer
    }

    /// <summary>Événement global du semestre.</summary>
    public enum GlobalEventType
    {
        OpenDay,            // Journée portes ouvertes : loyers doublés
        TransportStrike,    // Grève du RER : gares sans loyer
        BackToSchoolSale,   // Soldes de la rentrée : étages à moitié prix
        CareerFair          // Forum entreprises : +20 par terrain
    }

    /// <summary>Personnage jouable.</summary>
    public enum CharacterId
    {
        Major,      // Le Major de promo
        Partygoer,  // Le Fêtard du BDE
        Techie      // Le Techos
    }

    /// <summary>Pouvoir unique d'un personnage.</summary>
    public enum PowerType
    {
        DoubleRent,     // Dossier béton
        CancelPayment,  // Soirée mémorable
        Reroll          // Relance
    }

    /// <summary>Difficulté des IA, fixée par le niveau.</summary>
    public enum AIDifficulty
    {
        Easy,
        Normal,
        Hard
    }
}

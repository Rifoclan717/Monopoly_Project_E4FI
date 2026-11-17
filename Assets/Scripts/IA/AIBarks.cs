using System;
using System.Collections.Generic;

namespace CampusTycoon.AI
{
    /// <summary>Moment fort qui peut déclencher une réplique d'IA.</summary>
    public enum BarkMoment
    {
        Bought,             // l'IA achète un terrain
        MonopolyCompleted,  // l'IA complète un groupe
        RivalBought,        // un rival achète un terrain (réaction)
        Upgraded,           // l'IA construit
        RentCollected,      // l'IA encaisse un loyer
        RentPaid,           // l'IA paie un loyer
        SentToDetention,    // l'IA part en colle
        PowerUsed,          // l'IA utilise son pouvoir
        Bankrupt,           // l'IA fait faillite
        RivalBankrupt       // un rival fait faillite (réaction)
    }

    /// <summary>Réplique choisie : qui parle, à quel moment, et le texte final.</summary>
    public sealed class AIBarkLine
    {
        public readonly int Speaker;
        public readonly CharacterId Character;
        public readonly BarkMoment Moment;
        public readonly string Text;

        public AIBarkLine(int speaker, CharacterId character, BarkMoment moment, string text)
        {
            Speaker = speaker;
            Character = character;
            Moment = moment;
            Text = text;
        }

        /// <summary>Texte prêt pour la bulle : « Le Techos : « … » ».</summary>
        public string Formatted => AIBarks.SpeakerName(Character) + " : « " + Text + " »";

        public override string ToString() => Formatted;
    }

    /// <summary>
    /// Répliques des IA (C# pur, testable hors Unity).
    /// Choisit une phrase selon le personnage et le moment, sans répéter la précédente,
    /// et évite le bavardage : un délai minimal entre deux répliques, sauf pour les moments importants.
    /// Marqueurs dans les textes : {case} = nom de la case, {montant} = somme en jeu.
    /// </summary>
    public sealed class AIBarks
    {
        private readonly Random rng;
        private readonly double cooldownSeconds;
        private double lastBarkTime = double.NegativeInfinity;
        private readonly Dictionary<(CharacterId, BarkMoment), int> lastIndex = new Dictionary<(CharacterId, BarkMoment), int>();

        public AIBarks(int seed = 0, double cooldownSeconds = 3.0)
        {
            rng = seed == 0 ? new Random() : new Random(seed);
            this.cooldownSeconds = cooldownSeconds;
        }

        /// <summary>
        /// Renvoie une réplique, ou null si l'IA se tait (tirage, délai minimal, ou moment sans texte).
        /// </summary>
        /// <param name="now">Temps courant en secondes (Time.unscaledTime dans Unity).</param>
        public AIBarkLine TryBark(int speaker, CharacterId character, BarkMoment moment, double now,
                                  string squareName = null, int amount = 0)
        {
            bool important = IsImportant(moment);
            if (!important && now - lastBarkTime < cooldownSeconds) return null;
            if (rng.NextDouble() >= Chance(moment, amount)) return null;

            var lines = Usable(Lines(character, moment), squareName);
            if (lines.Count == 0) return null;

            int index = rng.Next(lines.Count);
            if (lines.Count > 1 && lastIndex.TryGetValue((character, moment), out int previous) && index == previous)
                index = (index + 1 + rng.Next(lines.Count - 1)) % lines.Count;
            lastIndex[(character, moment)] = index;
            lastBarkTime = now;

            string text = lines[index]
                .Replace("{case}", string.IsNullOrEmpty(squareName) ? "ce terrain" : squareName)
                .Replace("{montant}", amount.ToString());
            return new AIBarkLine(speaker, character, moment, text);
        }

        /// <summary>Une réplique qui parle d'un amphi n'est dite que sur un amphi.</summary>
        private static IReadOnlyList<string> Usable(IReadOnlyList<string> lines, string squareName)
        {
            bool onAmphi = squareName != null && squareName.StartsWith("Amphi", StringComparison.OrdinalIgnoreCase);
            var usable = new List<string>(lines.Count);
            foreach (var l in lines)
                if (onAmphi || l.IndexOf("amphi", StringComparison.OrdinalIgnoreCase) < 0) usable.Add(l);
            return usable;
        }

        /// <summary>Moments assez rares pour toujours mériter une réplique, même juste après une autre.</summary>
        public static bool IsImportant(BarkMoment moment) =>
            moment == BarkMoment.MonopolyCompleted || moment == BarkMoment.SentToDetention ||
            moment == BarkMoment.PowerUsed || moment == BarkMoment.Bankrupt || moment == BarkMoment.RivalBankrupt;

        /// <summary>Probabilité de parler. Les gros loyers font toujours réagir.</summary>
        public static double Chance(BarkMoment moment, int amount)
        {
            switch (moment)
            {
                case BarkMoment.Bought: return 0.7;
                case BarkMoment.RivalBought: return 0.3;
                case BarkMoment.Upgraded: return 0.5;
                case BarkMoment.RentCollected: return amount >= 40 ? 1.0 : 0.4;
                case BarkMoment.RentPaid: return amount >= 60 ? 1.0 : 0.35;
                default: return 1.0;
            }
        }

        public static string SpeakerName(CharacterId character)
        {
            switch (character)
            {
                case CharacterId.Major: return "Le Major";
                case CharacterId.Partygoer: return "Le Fêtard";
                default: return "Le Techos";
            }
        }

        public static IReadOnlyList<string> Lines(CharacterId character, BarkMoment moment)
        {
            return Library.TryGetValue((character, moment), out var lines) ? lines : Array.Empty<string>();
        }

        // ------------------------------------------------------------------
        // Textes. Major : ambitieux et agressif. Fêtard : chaotique. Techos : calculateur et prudent.
        // Les trois exemples du document de règles y figurent.
        // ------------------------------------------------------------------

        private static readonly Dictionary<(CharacterId, BarkMoment), string[]> Library =
            new Dictionary<(CharacterId, BarkMoment), string[]>
        {
            // --- Le Major de promo ---
            { (CharacterId.Major, BarkMoment.Bought), new[] { "{case} ? Évidemment que je la prends.", "Un pas de plus vers la bourse.", "Je collectionne les bonnes adresses." } },
            { (CharacterId.Major, BarkMoment.MonopolyCompleted), new[] { "Monopole ! Le campus m'appartient.", "Groupe complet. Préparez vos économies.", "Premier de la promo, premier propriétaire." } },
            { (CharacterId.Major, BarkMoment.RivalBought), new[] { "Profite bien de {case}, ça ne durera pas.", "Joli coup… pour un débutant.", "Note bien : je rachète toujours." } },
            { (CharacterId.Major, BarkMoment.Upgraded), new[] { "On passe à la vitesse supérieure.", "Un étage de plus, un loyer de plus.", "Construire, c'est gagner." } },
            { (CharacterId.Major, BarkMoment.RentCollected), new[] { "Merci pour le loyer !", "{montant}, c'est le tarif. Merci.", "Reviens quand tu veux, c'est payant." } },
            { (CharacterId.Major, BarkMoment.RentPaid), new[] { "Simple investissement dans ta défaite.", "Profite, c'est la dernière fois.", "{montant}… je note." } },
            { (CharacterId.Major, BarkMoment.SentToDetention), new[] { "Une colle ? Moi ? Erreur administrative.", "Je reviens plus fort.", "Même en colle, je reste premier." } },
            { (CharacterId.Major, BarkMoment.PowerUsed), new[] { "Dossier béton : ce loyer compte double.", "Mon dossier est irréprochable. Paie." } },
            { (CharacterId.Major, BarkMoment.Bankrupt), new[] { "Impossible… je demande une seconde session.", "Le Major ne perd pas. Il se retire." } },
            { (CharacterId.Major, BarkMoment.RivalBankrupt), new[] { "Un de moins dans le classement.", "La sélection naturelle du campus." } },

            // --- Le Fêtard du BDE ---
            { (CharacterId.Partygoer, BarkMoment.Bought), new[] { "Soirée chez moi ce soir !", "{case} ? Parfait pour une after.", "Je l'achète, on verra pour le loyer." } },
            { (CharacterId.Partygoer, BarkMoment.MonopolyCompleted), new[] { "Monopole ! Tournée générale !", "Tout le groupe est à moi, c'est la fête !", "Le BDE prend le pouvoir !" } },
            { (CharacterId.Partygoer, BarkMoment.RivalBought), new[] { "Tu m'invites à la crémaillère ?", "Sympa {case}, on y fait une soirée ?", "Ah, tu fais le sérieux maintenant ?" } },
            { (CharacterId.Partygoer, BarkMoment.Upgraded), new[] { "On agrandit la salle des fêtes !", "Plus de place pour danser !", "J'ai pris l'option sono." } },
            { (CharacterId.Partygoer, BarkMoment.RentCollected), new[] { "{montant} ! Ça paie les pizzas.", "Merci, c'est pour la cagnotte du BDE !", "Le loyer, c'est mon entrée à la soirée." } },
            { (CharacterId.Partygoer, BarkMoment.RentPaid), new[] { "Je paie, mais c'est toi qui invites ensuite.", "Aïe, adieu le budget soirée.", "{montant} ? On ne compte pas entre amis…" } },
            { (CharacterId.Partygoer, BarkMoment.SentToDetention), new[] { "La colle ? J'apporte l'enceinte.", "Encore ? J'ai juste mis un peu d'ambiance.", "Quelqu'un a des chips ?" } },
            { (CharacterId.Partygoer, BarkMoment.PowerUsed), new[] { "Soirée mémorable : on oublie cette dette !", "Quel loyer ? Je ne me souviens de rien." } },
            { (CharacterId.Partygoer, BarkMoment.Bankrupt), new[] { "Plus un sou… mais quelle soirée !", "Je suis ruiné. Quelqu'un m'offre un verre ?" } },
            { (CharacterId.Partygoer, BarkMoment.RivalBankrupt), new[] { "Pot de départ ce soir !", "Il nous quitte, on lui fait une fête ?" } },

            // --- Le Techos ---
            { (CharacterId.Techie, BarkMoment.Bought), new[] { "Rentabilité calculée. J'achète.", "{case} : rendement positif.", "Investissement validé par mes calculs." } },
            { (CharacterId.Techie, BarkMoment.MonopolyCompleted), new[] { "Monopole. Loyer multiplié par deux.", "Groupe complet, comme prévu.", "Statistiquement, vous allez payer." } },
            { (CharacterId.Techie, BarkMoment.RivalBought), new[] { "Tu vas regretter cet amphi…", "{case} ? Mauvais calcul.", "Achat sous-optimal, je le note." } },
            { (CharacterId.Techie, BarkMoment.Upgraded), new[] { "Niveau suivant : retour sur investissement assuré.", "Mise à jour du bâtiment effectuée.", "Le loyer suit une courbe croissante." } },
            { (CharacterId.Techie, BarkMoment.RentCollected), new[] { "{montant}, conforme à mes prévisions.", "Paiement reçu. Merci.", "Les probabilités ont parlé." } },
            { (CharacterId.Techie, BarkMoment.RentPaid), new[] { "Perte anticipée, réserve intacte.", "{montant}… c'était dans la marge d'erreur.", "Variance défavorable. Rien de grave." } },
            { (CharacterId.Techie, BarkMoment.SentToDetention), new[] { "Statistiquement improbable… et pourtant.", "Erreur de calcul. Je recompile.", "Pause forcée. J'optimise mon plan." } },
            { (CharacterId.Techie, BarkMoment.PowerUsed), new[] { "Relance : je garde le meilleur résultat.", "Nouveau tirage, nouvelles probabilités." } },
            { (CharacterId.Techie, BarkMoment.Bankrupt), new[] { "Mon modèle n'avait pas prévu ça.", "Erreur fatale. Fin du programme." } },
            { (CharacterId.Techie, BarkMoment.RivalBankrupt), new[] { "Prévisible depuis trois tours.", "Gestion des risques insuffisante." } },
        };

        /// <summary>Moments couverts, pour les tests.</summary>
        public static IEnumerable<BarkMoment> AllMoments() => (BarkMoment[])Enum.GetValues(typeof(BarkMoment));
    }
}

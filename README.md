# Campus Tycoon (Unity 6)

Jeu de plateau stratégique 3D inspiré du Monopoly sur le thème d'un campus universitaire, développé en équipe dans Unity 6 (URP, New Input System).

---

## Présentation rapide

- **3 niveaux progressifs** (`Assets/Data/Levels/`) :
  - **Niveau 1 — Première semaine** : 16 cases, 1 200 €, 12 tours, IA Facile.
  - **Niveau 2 — Mi-semestre** : 20 cases, 1 000 €, 15 tours, 2 événements du semestre, IA Normale.
  - **Niveau 3 — Semestre en folie** : 24 cases, 1 000 €, 15 tours, 3 événements du semestre, groupe Or + **Roulette du BDE**, IA Difficile.
- **3 personnages jouables aux pouvoirs uniques (1 fois par partie)** :
  - **Le Major de promo** — *Dossier béton* : double un loyer encaissé.
  - **Le Fêtard du BDE** — *Soirée mémorable* : annule un loyer ou une taxe à payer.
  - **Le Techos** — *Relance* : relance les dés et garde le second résultat.
- **Utility AI** complète (probabilités 2d6, évaluation des monopoles/blocages/liquidités, répliques dynamiques par personnalité) testée hors Unity via `Tools/AISimulation` (77 scénarios).
- **Vue 3D procédurale complète** : plateau généré depuis `LevelData`, bâtiments évolutifs (niveaux 0 à 3), pions animés par splines de Bézier quadratiques, dés 3D orientés par quaternions, caméra orbitale, inspecteur de case, HUD UGUI responsive et audio procédural (musique calme + SFX).

---

## Lancer le projet

1. Ouvrir le projet avec **Unity 6 (`6000.3.10f1`)**.
2. Ouvrir la scène principale : `Assets/Scenes/Scene_Main.unity`.
3. Cliquer sur **Play ▶️** : le **Menu Principal** s'affiche sur le plateau 3D vivant (choix du niveau 1 à 3 et du personnage).
4. *(Optionnel)* Vérifier la configuration de la scène via le menu Unity : **`Campus Tycoon > Vue 3D > Vérifier la configuration de la Scène`**.

### Lancer la suite de tests IA hors Unity

```bash
cd Tools/AISimulation
dotnet run -c Release 200
```

---

## Commandes en jeu

| Action | Clavier | Souris / UI |
| :--- | :--- | :--- |
| Lancer les dés | `Espace` | Bouton **Lancer les dés** |
| Passer l'animation des dés | — | `Clic gauche` sur le plateau |
| Décisions (achat, étage, pouvoir, Roulette, billet) | `O` (Oui) / `N` (Non) | Boutons **Oui / Non** |
| Sortir de Colle | `P` (Payer) · `B` (Billet) · `T` (Attendre) | Boutons dédiés |
| Inspecter une case (loyers, propriétaire) | — | `Clic gauche` sur n'importe quelle case 3D |
| Tourner / zoomer / recentrer la caméra | `A` / `Q` / `D` ou `←` / `→` · `R` (recentrer) | `Clic droit + glisser` · `Molette` |
| Mode Turbo / Son / Aide / Pause | `V` · `M` · `H` · `Échap` | Barre d'outils en haut à droite |
| Cerveau de l'IA : réglages et dernières décisions des IA, avec score et justification | `I` | — |

---

## Architecture MVC & Équipe

- **Modèle & Logique (`Assets/Scripts/Model/`, `Assets/Scripts/Core/`, `Assets/Data/`)** — *Franck* : ScriptableObjects, `GameState`, `RentCalculator`, `TurnManager` et bus d'événements `CampusEvents`.
- **Vue 3D, UI/HUD, Scènes & Audio (`Assets/Scripts/View/`, `Assets/Scenes/`, `Assets/Materials/`)** — *Mohamed* : génération procédurale du plateau/bâtiments/dés, caméra orbitale, splines, Menu Principal, HUD UGUI, inspecteur de case, audio procédural et packaging Build.
- **Intelligence Artificielle (`Assets/Scripts/IA/`, `Assets/Data/AI/`, `Tools/AISimulation/`)** — *Malick* : Utility AI (`AIBrain`, `UtilityScorer`, `BoardProbabilities`, `RentEstimator`), pont `AISnapshotBuilder`, répliques `AIBarks`/`AIBarkController` et simulateur de tests.


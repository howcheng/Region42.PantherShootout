# Panther Shootout Tournament — Business Rules

This document is a **self-contained** business-rules spec, written for an AI coding agent that
does NOT have access to the source code of the existing spreadsheet-generating application. All
rules are described in plain language; no source file/class names need to be looked up anywhere
else. It covers the two **separate, independently-scored competitions** that run at the tournament:

1. **Pool Play / Championship ("the games")** — round-robin games within a pool, plus (for
   most division sizes) a championship bracket.
2. **Penalty Shootout ("PSO")** — a separate penalty-kick shootout competition with its own
   standings, run in parallel with pool play, scored using a different points system and
   different tiebreakers.

Everything below is organized by division size, since the rules materially differ depending on
how many teams are in a division (4, 5, 6, 8, 10, or 12). A division always has either **3, 4, or
5 teams per pool** (see table below) and is placed into pools accordingly.

> NOTE: The existing spreadsheet-generating application is a console app that programmatically
> builds a Google Sheets workbook (one "Shootout" sheet plus one sheet per division) using the
> Google Sheets API; scores are entered by hand into that spreadsheet by tournament staff at the
> event, and formulas in the sheet compute standings/rankings live as scores are typed in. None of
> that spreadsheet mechanism carries over to the new app — only the **business rules** (how points,
> standings, tiebreakers, and brackets are computed) do. Clarifications obtained directly from the
> tournament organizer are called out inline as "Clarification" notes, and any remaining unresolved
> items are listed under "Open Questions" at the end.

## Glossary of abbreviations used below

| Abbrev. | Meaning |
|---|---|
| GP | Games Played |
| W / L / D | Wins / Losses / Draws |
| PTS | Standings points (see "Total points formula" below) |
| GF / GA / GD | Goals For / Goals Against / Goal Differential (GF − GA) |
| YC / RC | Yellow Cards / Red Cards (misconduct counts) |
| FORF | Forfeit flag on a game |
| PSO | Penalty Shootout (the separate competition) |
| PK | Penalty Kick |
| KFTM | "Kicks From The Mark" — a live, staff-run penalty-kick tiebreaker procedure (not the same as the PSO competition) |
| TB\* (e.g. TBH2H, TBW, TBC, TBGA, TBGD, TBPK) | "Tiebreaker" helper values used only to rank/sort teams; not shown to spectators as standalone stats |

## Divisions

There are 6 divisions in the tournament, each an age/gender combination:
- 10U Girls, 10U Boys, 12U Girls, 12U Boys, 14U Girls, 14U Boys

Each division independently has one of the team-count configurations below (4/5/6/8/10/12 teams).
In the current app, team-to-division-and-pool assignment comes from an uploaded CSV file listing,
for every team: division name, team name, and pool name (letter/number identifying which pool
within the division the team belongs to). The new app will similarly need team/division/pool
assignment as input, plus (per the clarifications below) an uploaded game schedule.

## Division size configuration matrix

| # Teams | Teams/Pool | # Pools | Game rounds (pool play) | Games/round | Shootout rounds | Championship bracket? |
|---|---|---|---|---|---|---|
| 4  | 4 | 1 | 3 | 2 | 3 | Yes |
| 5  | 5 | 1 | 5 | 2 | 4 | **No** |
| 6  | 3 | 2 | 3 | 1 | 4 | Yes |
| 8  | 4 | 2 | 3 | 2 | 3 | Yes |
| 10 | 5 | 2 | 5 | 2 | 4 | **No** |
| 12 | 4 | 3 | 3 | 2 | 3 | Yes |

- **"Game rounds"** = number of rounds of *pool-play* round-robin games per pool.
  - A pool of *n* teams normally plays `n - 1` round-robin games (`TotalPoolPlayGames = TeamsPerPool - 1`).
  - Pools of 5 teams play 5 rounds because with an odd pool size, one team has a bye each
	round, so it takes 5 rounds (not 4) to complete the round robin (2 games per round; the 5th
	team sits out).
- **"Games/round"**: how many simultaneous games are entered per round (`GamesPerRound`). For a
  pool of 3 teams, only 1 game can be played per round (one team has a bye); pools of 4 or 5
  play 2 games per round.
- **"Shootout rounds"** governs the separate PSO scoring sheet, described in its own section
  below; it is *not* the same count as pool-play game rounds. Divisions with 3- or 5-team pools
  (6, 5, 10 teams) get an extra 4th shootout round because of the bye rotation (see PSO section).

## Team names
Team names follow the format `<CODE> <REGION>: <COACH>`. The code is a combination of pool name
and team number. Pools are identified by letters, so team A1 is the first team in pool A. The 
numbers are arbitrary and do not signify anything.

## Pool Play (Games) Rules

### Round-robin scheduling
- Teams within a pool play every other team in the pool once (standard round robin).
- In the **current** spreadsheet-generating app, home/away designation and pairings per round are
  not computed — it just creates blank rows with dropdown validation for "Home" and "Away" team
  names, to be filled in manually by tournament staff at the event.
- **Clarification (new app):** Unlike the current app, the **new web application must generate
  the round-robin pairings/schedule per round itself.** Tournament staff will upload a file
  containing the full game schedule (all games, all rounds, for all divisions/pools) ahead of
  time; the app consumes that schedule rather than staff manually picking teams from a dropdown
  per game slot at data-entry time.
- **Clarification (byes):** Because the game schedule (including which team has a bye each round,
  for odd-sized pools of 3 or 5 teams) comes from the uploaded file, **the new app does not need
  any logic to compute or infer byes** — the uploaded schedule is the source of truth for which
  teams play (and, by omission, which team sits out) each round.

### Per-game score entry fields
Each pool-play game has, at minimum:
- Home team (chosen from the division's team list)
- Away team (chosen from the division's team list)
- Home goals, Away goals
- Forfeit checkbox (`FORF`) — when checked, indicates the game was decided by forfeit rather
  than play. **When a forfeit occurs, score must still be entered as 1–0** (see remarks in
  `PsoFormulaGenerator.GetPsoGamePointsFormulaForHomeTeam`), with the forfeit checkbox checked
  so that goal is not counted as a real goal (see Game Points and Tiebreakers rules below).
- Yellow cards (`YC`) and Red cards (`RC`) — counts per team, used for the misconduct tiebreaker
  and total-points deduction (see below).
- A computed "Winner" result for the game: **Home** if home goals > away goals, **Away** if away
  goals > home goals, **Draw** if they're equal, and undetermined/blank if either score hasn't
  been entered yet. This is the full extent of the winner-determination logic — there is nothing
  more nuanced than a plain goals comparison (confirmed by the tournament organizer; forfeits
  still just result in an entered 1–0 score as described above, they don't change how "winner" is
  computed).

### Game points (pool-play "PTS" per game, used to build a game's contribution to the standings' PTS column)
Each team earns per-game points as follows:
- Win = **6 points**, Draw = **3 points**, Loss = **0 points**.
- **+1 point per goal scored, capped at 3** (i.e., goals beyond the 3rd don't earn extra points).
- **+1 "shutout" bonus point if opponent was held to 0 goals**, UNLESS the game was decided by
  forfeit (forfeit checkbox/flag set), in which case no shutout bonus is given.
- If the game hasn't been played/entered yet, it contributes 0 points (and doesn't count toward
  games played).
- Examples:
  - Team A beats Team B 2–0: Team A gets 6 + 2 + 1 = 9 points (win + 2 goals + shutout bonus),
	Team B gets 0 points (loss).
  - Team A beats Team B 4–1: Team A gets 6 + 3 + 0 = 9 points (win + 3 goals [capped] + no shutout),
	Team B gets 1 point (loss + 1 goal).
  - Team A draws Team B 2–2: Both teams get 3 + 2 + 0 = 5 points (draw + 2 goals + no shutout),
  - Team A draws Team B 0–0: Both teams get 3 + 0 + 1 = 4 points (draw + 0 goals + shutout bonus).
  - Team B forfeits to Team A (1–0, forfeit checkbox checked): Team A gets 6 + 1 + 0 = 7 points (win + 1 goal),
	but that goal does **not** count toward the goal differential tiebreaker (see tiebreaker #4 below). Team B gets 0 points (loss).

### Total (standings) points formula — "PTS" column in standings table
- Standings `PTS` = **sum of the per-game points above across all rounds/games for that team**
  **MINUS 1 point per yellow card MINUS 2 points per red card** for that team (note the
  asymmetric weighting here — red cards cost double what they cost yellow cards for this specific
  deduction, which is different from the misconduct **tiebreaker**, where YC and RC are weighted
  equally; see tiebreaker #3 below).
- **Clarification (cards):** In the current spreadsheet, yellow/red cards are entered as a single
  **cumulative running total per team** for the whole pool (there is only one standings row per
  team, not one per game, so cards can't be tracked per-game there). **In the new app, cards
  should be entered per-game** (i.e., a `PoolPlayGame` records yellow/red cards issued to each of
  the two teams in that specific game), and the team's total YC/RC for standings/tiebreaker
  purposes is the sum across all of that team's games in the pool.

### Standings columns (per pool)
Each pool has one standings table listing, per team: `GP` (games played), `W`, `L`, `D`, `PTS`,
`GF`, `GA`, `GD`, `RANK`, cumulative `YC`/`RC` (see per-game vs. cumulative clarification above),
and a `FORF` indicator (whether any of the team's games were decided by forfeit).

### Tiebreakers (pool standings) — applied in this order
When teams are tied on `PTS` (standings points, the primary sort key — highest PTS ranks best),
the following tiebreakers are evaluated **in this exact order** until the tie is broken:

1. **Head-to-head result** — descending (win counts as "better" than a loss). The result of the
   single game played directly between the two tied teams. This only applies to a
   2-way tie; with 3+ teams tied, fall through to the next tiebreaker.
2. **Games won** — descending (more wins is better). **Only used if NO game in the pool (or,
   for 10-team divisions which span two 5-team pools, anywhere in the whole division) was
   decided by forfeit.** If any forfeit occurred anywhere in that scope, this tiebreaker is
   neutralized (effectively skipped/treated as equal for everyone, falling through to the next
   tiebreaker).
3. **Misconduct (cards)** — ascending (fewer cards is better). Least number of red cards, followed
   by least number of yellow cards, across all of the tied team's games in the pool. If one team has
   two yellow cards, and another has one red card, the team with the single red card ranks worse (more 
   misconduct) than the team with two yellow cards.
4. **Goals against** — ascending (fewer goals conceded is better). Sum of goals conceded across
   the pool's (or division's, for 10-team) games, **excluding any game decided by forfeit**
   (a forfeited game's "1 goal against" does not count toward this total).
5. **Goal differential** — descending (higher GF − GA is better). **Only used if there were NO
   forfeits anywhere in the relevant scope** (same forfeit-nullification rule as tiebreaker #2);
   if any forfeit occurred, this tiebreaker is neutralized and falls through.
6. **KFTM (kicks-from-the-mark) manual result** — descending (a team credited with the KFTM win
   ranks above the other tied team(s)). See the Glossary and the Resolved Clarifications section
   for what KFTM is; this is a live procedure result entered manually by tournament staff, used
   only when teams are still tied after all 5 formula-based tiebreakers above.

Note: the goals-against and goal-differential tiebreakers use per-game caps that differ from
each other (and from the plain `GA`/`GD` standings columns):
- **Goals against (TB)**: each game's goals conceded are capped at **5** before summing
  (a 7–0 loss contributes 5 toward tiebreaker GA).
- **Goal differential (TB)**: each game's goals scored are capped at **3**, then that total
  minus the capped goals-against total above (i.e. `sum(min(GF, 3)) − sum(min(GA, 5))` over
  non-forfeit games). A 7–0 win contributes `3 − 0 = +3` toward tiebreaker GD for the winner
  and `0 − 5 = −5` for the loser.
These are **separate** from the uncapped standings `GA`/`GD` columns. **Both the capped
tiebreaker-only GA/GD totals and the uncapped standings `GA`/`GD` columns need to exist** as
distinct values in the new app.

### Rank calculation
- A team's overall `RANK` within its pool is its position after sorting all teams in the pool by
  PTS (descending) and then applying the 6 tiebreakers above, in order, to any teams still tied.
- Rank should only be considered final/displayed once **all teams in the pool have played their
  required number of pool-play games** for that pool size (see the division-size matrix above for
  how many games each pool-size plays); before that point, standings are still "in progress" and
  final placement isn't authoritative.

## Championship Bracket rules (by division size)

**Divisions of 5 and 10 teams have NO championship round** — the division winner is simply
whichever team finishes #1 in pool-play standings (for 10 teams, ranked across both 5-team pools
combined by total PTS — see the 10-team note below).

### 4 teams (1 pool of 4)
- **Consolation**: 3rd place vs 4th place (by pool standings rank).
- **Championship**: 1st place vs 2nd place.

### 6 teams (2 pools of 3)
- **Consolation**: 3rd-place team from Pool A vs 3rd-place team from Pool B.
- **Semifinal 1**: 1st place Pool A vs 2nd place Pool B.
- **Semifinal 2**: 1st place Pool B vs 2nd place Pool A.
- **3rd-place game**: loser of Semifinal 1 vs loser of Semifinal 2.
- **Final**: winner of Semifinal 1 vs winner of Semifinal 2.

### 8 teams (2 pools of 4)
- **3rd-place game**: 2nd place from Pool A vs 2nd place from Pool B.
- **Championship**: 1st place from Pool A vs 1st place from Pool B.
- (No semifinal round — the "3rd place" and championship games are the only bracket games;
  1st-place teams go straight to the final and 2nd-place teams play for 3rd.)

### 10 teams (2 pools of 5) — no championship (see above)
Standings/winner is determined solely by pool play, ranking across **both pools together** (by
total PTS, then the same 6 tiebreakers as above but evaluated division-wide instead of per-pool)
to determine the overall division "winner" and other placements.

### 12 teams (3 pools of 4)
- Each of the 3 pools produces a 1st-place ("pool winner") and 2nd-place ("runner-up") team.
- All 3 pool winners are pooled together and ranked 1st/2nd/3rd amongst themselves by PTS (with
  their own parallel tiebreaker sub-system, mirroring the 6-level tiebreaker order above but
  scoped only to these 3 teams' head-to-head/records against everyone they played). Likewise, the
  3 runners-up are pooled together and ranked amongst themselves the same way.
- **3rd-place game**: 3rd-ranked pool winner vs. 1st-ranked (best) runner-up — i.e., the
  highest-ranked runner-up "moves up" to play the lowest-ranked pool winner for 3rd place.
- **Championship**: 1st-ranked pool winner vs. 2nd-ranked pool winner.
- **Clarification:** Only the 3 pool winners plus the single best-ranked runner-up (4 teams total)
  advance to the bracket above. **All other teams — the 2nd- and 3rd-ranked runners-up — are
  eliminated with no further bracket game.** This is confirmed intentional by the tournament
  organizer, not a gap to be fixed in the new app.

### Championship-game winner display
Whichever team's standings-table row corresponds to the winning side of a bracket game should be
visually/logically flagged as the winner of that bracket slot (used for bracket display purposes).
For the 5/10-team divisions (no bracket), the equivalent "division winner" flag is simply the #1
ranked team once all required pool games are complete.

No additional/hidden logic exists for determining an individual game's winner beyond the
straightforward higher-goals-wins comparison described in "Per-game score entry fields" above —
confirmed by the tournament organizer.

## Penalty Shootout (PSO) Competition Rules

This is a **completely separate scoring competition** run alongside the games, with its own
"Shootout" master sheet listing every team across every division. It uses penalty-kick shootout
results (not the pool-play game scores) as its data source.

### Structure
- Each team gets up to 3 or 4 PSO shootout "rounds," **one PSO round per pool-play game round**
  for that division/pool: normally 3 rounds (4-team pools, i.e. 4/8/12-team divisions), or **4
  rounds** when pools have an odd number of teams (3-team pools in 6-team divisions, or 5-team
  pools in 5/10-team divisions) — because with a bye rotation, it takes one extra round for every
  team to get its 3 actual shootout matchups (one team sits out each round, same as in pool play).
  - For 5-team pools specifically, a team's 4th shootout round only counts toward its total
	**if it hasn't already accumulated 3 rounds' worth of PSO results** — i.e., a team that
	happened to get its 3 shootout matchups within rounds 1–3 doesn't also get a (nonexistent)
	4th one counted; this avoids double counting for teams unaffected by that particular bye.
- The PSO shootout for a given pairing of teams happens **alongside/after the corresponding
  pool-play game** between those same two teams — so PSO round 1 pairs the same two teams as
  pool-play game round 1 for that pool, PSO round 2 mirrors pool-play round 2, etc. Each PSO round
  records: home team, home PK goals, away PK goals, away team.
  - For 6-team divisions, the 3rd shootout round will take place during the championship rounds because
	during pool play, each team only plays 2 games in the round-robin (one per round).
- Each team's PSO total is simply the sum of the PK goals it scored across all of its PSO rounds
  (3 or 4, depending on pool size).
- A round's PSO result for a team should only be treated as "final"/counted once **both scores for
  that round have actually been entered**, and (for pools with a bye) only for teams that actually
  had a shootout matchup in that round (i.e., excluding the team with the bye that round).

### PSO Points / Ranking
- Primary sort is simply **total PK goals scored across all rounds** (higher is better).
- **Tiebreakers for PSO ranking**, evaluated in this order until the tie is broken:
  1. **Goals against (in the shootout)** — ascending (fewer PK goals conceded across the team's
	 shootout matches is better), using the same "don't double-count an unaffected team's 4th
	 round" rule described above for 5-team pools.
  2. **KFTM manual result** — descending, used when teams are still tied after the goals-against
	 tiebreaker. **This is a separate, independent live tiebreaker/flag from the pool-play (game)
	 standings' KFTM tiebreaker** — even though both are conceptually "a live PK shootout run by
	 staff to break a tie," a KFTM result recorded for the pool-play standings has no bearing on,
	 and is not shared with, the KFTM result (if any) recorded for the PSO standings, and vice
	 versa. Model these as two independent manual-override values.

### PSO scope note
This is central to the domain and easy to get wrong: **PSO scoring/tiebreakers/ranking are
scoped to the whole division** (i.e., all teams in a division are ranked together against each
other for PSO purposes, regardless of which pool they're in), whereas pool-play
standings/tiebreakers are scoped **per pool** — except that (a) the "games won" and "goal
differential" no-forfeit nullification rule for 10-team divisions is evaluated across the whole
division (both 5-team pools) rather than a single pool, and (b) 12-team divisions have an extra
cross-pool "pool winners" / "runners-up" mini-ranking layered on top of the normal per-pool
standings, as described above.

## Data Model Implications for the New App (suggested, not prescriptive)

Entities to consider: `Division` (name, team-count/size config), `Pool` (belongs to Division),
`Team` (belongs to Pool), `PoolPlayGame` (round #, home/away team, home/away goals, forfeit flag,
yellow/red cards for each team), `ShootoutGame` (linked 1:1 to a `PoolPlayGame`, home/away PK
goals), `ChampionshipGame` (bracket slot label, references two team "sources" which may be
standings-rank-based lookups rather than fixed teams), and a manual `TiebreakerOverride`
(the `TBPK`/kicks-from-the-mark checkbox concept) per pool (or per division for PSO) usable when
formula-based tiebreakers are exhausted.

## Resolved Clarifications (from tournament organizer)

1. **Round-robin schedule generation**: The new app generates pairings per round; tournament
   staff upload a file containing the full game schedule ahead of time.
2. **Bye handling**: Not needed — byes are implicit in the uploaded game schedule file; the app
   has no rule of its own for determining which team has a bye.
3. **Card tracking**: Cumulative-only in the current spreadsheet (one standings row per team per
   pool). The new app will track yellow/red cards per game instead, summed for standings/points.
4. **KFTM tiebreakers**: Live, staff-conducted shootouts used only when teams remain tied after
   all formula-based tiebreakers. The pool-play ("games") KFTM tiebreaker and the PSO KFTM
   tiebreaker are independent of one another.
5. **12-team bracket**: Confirmed — only the 3 pool winners and the single best-ranked runner-up
   advance to the championship bracket; all other teams are eliminated with no consolation game.
6. **Game winner determination**: No additional logic beyond a straightforward goals-comparison;
   nothing further to account for.
7. **Tournament winner scope**: Winners/placements are calculated independently per division;
   there is no single overall tournament-wide winner.

## Open Questions (still outstanding)

None at this time — all prior open questions have been resolved above. If further implementation
details surface during the new app's design (e.g., exact file format for the uploaded game
schedule), they should be added here.

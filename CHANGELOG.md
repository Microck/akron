# Changelog

All notable user-facing changes to Akron should be recorded here.

This project uses version tags that match the mod version in `everest.yaml`, while release headings can use readable public names such as `Akron Beta 42`. Keep release notes focused on player-visible behavior, public docs, packaging, `.akr` file contracts, and migration notes when they matter.

## Unreleased

### Added

- Add a **Disable Playback** toggle to the Level tab, which turns off the demonstration ghosts some rooms place to show a move. The ghost stops moving, stops being drawn, stops leaving a trail, and stops playing its footstep, jump and dash sounds. It is classified Normal clear, so turning it on takes an attempt out of Goldberry/Hardlist clear and does not mark it as a cheat. Farewell's wave-dash tutorial pages and the recording preview use the same ghost outside a room, and this option leaves both alone.

- Read the other StartPos slots of the current map in the background, so the next slot on that map does not repeat the snapshot read that makes the first load slow. Measured on the test machine, a slot served from the cache loads in 2.7 s against 5.2 s from disk. The reading is queued after your load has finished, so it cannot slow down the load you are waiting on, and it only makes progress while you are not in control of the game. Three breaks were measured on the test machine, with the player running, jumping, dashing and dying throughout: a pause, the chapter select or overworld, and the wait-for-input window. The overworld read a slot every 2.9 s until the queue was empty, the input wait every 2.1 s - fast enough to empty a fourteen-slot queue inside a single wait - and twenty-one one-second pauses read nine slots between them. Fifty-six seconds of play with thirteen slots waiting decompressed nothing at all. A chapter change lifts the same gate, but no slot has ever been seen to be read during one: twelve chapter changes read none, so a transition is over before one slot can be read, and entering a different chapter empties the cache on arrival anyway. It also means a map warms over several of those breaks rather than all at once, so loading one slot straight after another with no pause in between still reads both from disk.
- Raise the memory limit on those background reads from 96 MB to 512 MB of decompressed save data. The old limit was smaller than one modded map's saved state, so on the maps with the slowest loads not a single slot was ever read ahead. Measured on a modded map, a slot held in this cache costs about 3.8x its size in real process memory, which puts a completely full cache at about 1.9 GB on top of the roughly 1 GB Celeste with mods already uses. That is the ceiling the limit is chosen against - a full cache measured directly, on a vanilla map, came in at 1.4 GB - and it is why a full fifteen-slot map does not fit on every map: two of the largest modded slots fit, or six to twelve vanilla ones, and the rest are read on demand. The cache empties as you load slots and when you leave the map.
- Raise the size limit on a single StartPos saved state to 384 MiB of expanded data. The largest saved state measured across 17 real installs expands to 220 MiB, and the released 192 MiB limit refused it before reading it, checked on the test machine against a build carrying the released limit. Akron now reads that file through and rebuilds it from disk. Reading it is what this changes and all it changes: the one saved state that large on the test machine still fails to restore afterwards, on a room object that has nothing to do with its size, and importing a setup pack that holds a saved state this large has not been checked. The new limit is 1.7x that measurement.
- Report the read-ahead cache in the Akron log and in `akron_status`. Each StartPos load says whether its saved state came from the cache or from disk, the background reader accounts for every slot it queued - read, already cached, too large for the remaining budget, unreadable, or not reached before the queue was replaced - and `akron_status` carries the slot count, bytes held, budget and queue.
- Take Celeste's forced garbage collection off death with a new **Defer Engine GC** option in the Global tab, on by default. Celeste runs a full blocking collection every time you die and every time a room reloads, which freezes the game for about a quarter of a second each time on the test machine. Akron now skips it while you play and runs it once at your next StartPos load, so a run of deaths costs one collection at a moment you were already waiting through. Turn the option off to put Celeste's behavior back, which is worth trying first if you see anything odd about memory or textures.

### Changed

- **Set your StartPos slots again, and re-export any `.akr` setup packs you share.** Every StartPos slot saved by an earlier build, and every `.akr` pack made by one, is refused by this build instead of loaded. Akron saves a StartPos by recording where each object sits in a freshly loaded copy of the room, and two fixes in this release changed what that fresh copy contains: the room's trails are now cleared before it is rebuilt, and the demonstration ghost is now left out of the saved state. An older slot counts objects that are no longer there, so its positions are off by one from the room this build builds. That does not always fail loudly - two objects of the same kind can quietly swap states and the load reports success. The saved state also has to carry two things it never carried before, because a load cannot work them out for itself: whether each saved resource is named by its key or only labelled by it, and whether the map laid a saved object's id out when the slot was set. A state written without them reads as if the answer to both were no, which is the weaker load this release replaces, so it cannot be read under the new name. For both reasons the saved-state contract moved from `akron-reconstruction-v7` to `akron-reconstruction-v9` and the pack contract from `akron-setup-v4` to `akron-setup-v6`, and anything older is refused rather than read. A slot left behind this way disappears from the slot list; loading it says it was saved by an older Akron that built rooms differently and to set it again. Importing an old pack names both contracts and asks for the pack to be exported again from this build. There is no conversion: an old saved state does not describe the room this build loads, so there is nothing correct to convert it into. Your slot positions, spawn settings and keybinds are untouched; it is the saved room state behind each slot that has to be captured again.

- Drop the `counters` block from the performance JSONL records, along with the eleven non-overlay `buckets` names and the two counter budgets in `scripts/akron-perf/report.mjs`. Nothing in the mod ever wrote any of them, so every one of those fields was a structural zero and the two `--gate` checks built on them could not fail. The overlay phase timings, the frame histogram and the GC attribution are unchanged and are what those reports were actually reading.
- Reorder the Logging levels so **Diagnostic** is quieter than **Verbose**. Diagnostic keeps the 60-second summary lines, Verbose adds one line per feature use, and Trace adds one line per policy check. Logs captured on Verbose now contain more than they used to.
- Say which mod is behind a StartPos that will not load. A load is refused when the room Akron rebuilds does not match the room the game just loaded, and what you saw was the reason Akron refused it: a graph path ten levels deep and thirty flags. It is exactly what a bug report needs and there is nothing in it you can act on. The case this was measured against is Extended Variant Mode, whose zoom variant only installs itself while that mod's master switch is on, so a slot set with the switch on genuinely cannot load with it off - Akron is right to refuse it, and `reconstructed type is not authentic to the fresh room;type=ExtendedVariants.Variants.ZoomLevel+<>c;path-depth=10` does not tell you to turn the switch back on. The message now reads `StartPos 3 needs ZoomLevel from ExtendedVariantMode, and this room does not have it. Check that mod's settings, or set the slot again.` A mod Akron can no longer load at all is named the same way, with turning it back on as the fix. When the object came from Celeste, Everest or Akron itself, the message says that no mod owns it and asks for a bug report if your mods have not changed, because that one is not yours to fix. One refusal is not about the object at all and does not go through any of that: a slot is refused when this room's map no longer places an entity the slot saved, which is what a map update or a collab version bump does. The entity's type explains nothing there, and reading it as one told a player who had just updated a collab that a `Refill` was an Akron bug and to send in a log, with no mention of the fix. It now reads `StartPos 1 could not be rebuilt: this map no longer places the Refill the slot saved. Updating a map or a collab does this. Set the slot again.`, and it says that whether the entity came from Celeste or from a helper, because the entity is not what changed. When Akron cannot tell which of those it is - a mod that ships several code files, say - it keeps showing the old text rather than guessing at a mod name. Which slots load is unchanged; the full path and every flag still go to `akron-current.log`, on the line above the one the game put on screen.

### Fixed

- Stop a failed StartPos Set from deleting the saved state the slot already had and then reporting that the slot was kept. Setting over an occupied slot writes the new saved state into a temporary folder, moves the slot's existing file aside into that folder, and moves the new file into place, so anything that goes wrong can be undone. The move that carries the old file aside is the one most likely to fail: the temporary folder sits on the system temp volume while your saved states live beside your save files, so on most installs that move is a copy of the whole file, and it fails when the temp volume is full, when the folder cannot be written to, or when something else holds the file open. A copy that fails leaves the file it was copying alone, so at that moment your saved state was still in the slot and still loadable. Akron deleted it, because the undo worked out what to reverse by looking at the folder afterwards: nothing moved aside read as "the file in the slot is the one this Set just put there", when nothing had been put there. The message that followed said the previous StartPos was kept, so the slot went on working from memory until you left the map and was gone at the next launch, and the one thing you were told about it was the opposite of what happened. The undo now acts on what the Set did rather than on what it can see afterwards: it puts the old file back when it really did move it aside, removes the new file when the slot was empty beforehand, and leaves the slot alone when it never got that far. A Set that fails for any other reason is unchanged, and a Set that works is untouched. Second half of the same fix: when the undo cannot put the old file back either, which is what a locked snapshot file does, the message now says the slot works until you leave the map and then has to be set again, rather than claiming it was kept. Measured in the test suite. This has not been checked in game.

- Stop a restore from taking away a save file the backup it is restoring never held. A backup that cannot open one of your files finishes anyway rather than costing you the rest of the folder, and records the files it had to leave out inside the archive. Nothing read that record back. A restore replaces your Saves folder by moving what is there aside, moving the archive's copies in, and then discarding what it moved aside, so restoring a backup that could not read `0.celeste` moved your `0.celeste` out, put nothing back, and discarded it - with no message, and recoverable only from the `pre-restore` backup by a player who worked out for themselves what had happened. Akron now refuses to restore a backup that is missing files, names them, and says so in the backup browser next to the backup itself, so you can pick a different one before you start. This is the same check the `pre-restore` backup has always been held to; it now applies to the backup you picked as well, and it runs before anything else so a refused restore does not leave a safety backup behind to explain. A backup that cannot say what it holds at all is refused for that reason instead, because not knowing is not the same as nothing being missing. That covers a ZIP with no `_akron-backup.json` in it, which used to read as a backup that was missing nothing: Akron writes that file into the archive last, so a backup interrupted part way through leaves a readable ZIP without one, and any other ZIP you put in the backup folder is listed and offered for restore just the same. Restoring one of those would have taken away every save file it did not happen to hold. The check reads the archive on disk at the moment you confirm rather than what the browser last listed, so a file that has changed since the list was drawn is judged as it is now. Your save files are never touched by a restore that refuses. The archive is an ordinary ZIP in a folder the Backups tab opens, so anything a refused backup does hold is still there to take out by hand. Measured in the test suite. This has not been checked in game.

- Stop a StartPos load from telling you your map changed when Akron could not read your map. A load is refused when this map no longer places an entity the slot saved, and the message says a map or collab update did it. Akron reads that from the map data the game holds, on the background thread that writes the saved state, and there are moments where there is nothing to read: a map reload in flight, or a chapter whose entry is not in the loaded list. Those answered the same as a map that really had dropped the entity, so the refusal could have named a map update that never happened. It now falls silent instead and the slot loads on its own merits, and the refusal is kept for a map that was read and really does place the entity differently. The same read can no longer fail the whole slot with a bare exception name either: it answers "nothing to read" instead of throwing when the map is rebuilt underneath it. No refusal you can see today changes; this closes the one whose failure mode was a wrong explanation. Measured in the test suite. This has not been checked in game.

- Say the plain sentence when Celeste closes before a StartPos restart copy is finished. Quitting stops the copy in progress and the slot is reported as not saved, which is right, but the reason read `$: OperationCanceledException: Celeste closed before its restart copy finished` - the sentence with a type name and a graph path bolted on the front. It now reads as the sentence. Nothing about which slots are saved changes.

- Stop a StartPos load from handing one room object's parts to another and calling it a success. Loading a slot rebuilds any saved object the freshly loaded room did not build - one a mod's loader skipped this run, or one the room now numbers differently - and to decide whether rebuilding one is safe Akron counted how many objects of that kind the fresh room has in that kind of place. A count cannot say which of them, so when two objects of one kind drew on one count the answer came down to the order they happened to sit in the saved state: one order refused the load, the other accepted it and left the room wrong. Measured on a room with two trailing objects, which is the shape any pair of dashing entities makes because a trail holds the very sprite it was drawn from: the accepted order dropped the object the room had just built, put a rebuilt copy in its place carrying the saved state, and left the surviving trail drawing the room's own sprite at the rebuilt copy's position, reported as a successful load. Akron now refuses a rebuild that would take a live object away from another object the same slot says is still in the room, whichever order the saved state lists the two in. Loads that rebuild an object beside the ones the room did build are unchanged, and so are loads where the room had cached something the saved frame had already destroyed. Measured in the test suite. This has not been checked in game.

- Refuse a StartPos saved state that keeps an object the load can never put anywhere, instead of loading the rest of it and reporting success. A saved state names each object by the slot it sits in, and Akron reads back exactly one kind of slot per object: an ordinary object's named fields, an array's elements, a callback list's targets. A hand-edited or corrupted file can put an object somewhere else - inside a live resource the room supplies itself, inside a counter Akron deliberately ignores, inside a packed number grid that is copied as raw bytes - and the completeness check accepted it, because that check looked in every slot while the load only ever reads one of them. The result was a load that reported success with the object built, filled with its saved state and attached to nothing, and the same dead slot was also read as evidence about which of the room's own objects a saved object should be paired with, which is what decides whether a load is allowed to hand over a live object at all. Akron now refuses such a file when it opens it and says which object could not be placed. This cannot happen to a state Akron wrote: a save only ever fills the one slot per object that a load reads back, so every slot the completeness check follows is now one the load writes. It matters because `.akr` setup packs are passed between players, so a state that did not come from your own game is an ordinary case rather than a hypothetical: a pack damaged in transit that used to leave a hole in the room is now refused, when it is imported into a different slot number and otherwise when the slot is loaded, in both cases before anything in the room is touched. Measured in the test suite. This has not been checked in game.

- Take back the disk space held by StartPos saved states this build can no longer read. Akron keeps each slot's saved room state in its own file under `Saves/AkronStartPos`, and the file name carries the format that wrote it. Nothing had ever removed one, so every time that format moved, one file per slot was left behind for good: unreadable, invisible, and never smaller. On the machine this was measured on the folder had reached 238 MB, and the files this release's format move left behind average 1.8 MB each. Akron now removes them in the background on the next launch. Only a file whose name carries a format version older than the one this build writes is removed, so a slot you can still load is never touched, and a file left by a newer build you downgraded from is left where it is. Nothing you can load changes. A slot the format move emptied still names the move when you load it and asks you to set it again. That answer no longer comes from the file being removed: your slot list now records which saved-state format each slot was written under, so it survives the file, the launch after it, and every later move. A slot whose saved state went missing while the format stayed put, from a file deleted by hand or a backup restored over the folder, says only that the state behind it is missing, because that is all anything here knows. This has not been checked in game.

- Stop Akron from saving a StartPos that can never be loaded back. If anything in the room held a raw memory address, such as a native handle belonging to a mod, Akron wrote that address into the slot's restart copy and reported the copy as saved. An address from one run of the game means nothing in the next one, so the slot kept loading until you left the chapter and then failed for good, with `InvalidCastException: Invalid cast from 'System.String' to 'System.IntPtr'` and nothing in it to say which object it came from. Akron now refuses the restart copy while it is being made and names the field it refused, along with the type that holds the address when the address sits in a field of one, so the slot is emptied or kept exactly as it is for any other failed restart copy and you are told when you set it rather than when you need it. A restart copy written by an earlier version that already holds one of these is refused by field path instead of showing the raw error. No object in Celeste, Everest or Akron itself is known to hold an address a room can reach; this was seen on Windows on an install with about fifty mods. The fix has not been checked in game.
- Keep StartPos slots loading after a chapter change when a mod stores text in a collection that sorts or matches by a language's rules. The change above refuses to save a StartPos that holds a raw memory address, and on the Windows install it was found on that refusal fired on an ordinary chapter 1 slot, because the address belonged to .NET rather than to any mod. A list, set or dictionary of text built to follow a language's alphabet, or to ignore case the way that language does, reaches one of these in two steps: .NET remembers where it loaded that language's sorting rules and keeps the address to hand. Collections that match text exactly, ignoring case or not, are not involved. The address describes nothing about your game, and .NET opens those rules again by itself on request. Akron now leaves the address out of the saved state, records which language's sorting the slot was using, and has the freshly loaded chapter open that same sorting from the rules installed on your machine. Slots on an install like that can load again, including across a chapter change, which is the only point this ever showed, because it is the point where Akron has to read the slot from disk rather than from memory. Addresses that Akron still reaches during a save are still refused, because those name one particular object rather than something the new run can look up for itself. A collection that keeps its entries in sorted order comes back sorting and matching the way it did before. A hash-based set or dictionary needed a second fix before it came back usable, which is the entry below. This was measured in the test suite, including a real two-run round trip through a file, and has not been checked in game.
- Make a StartPos load bring back sets and dictionaries that can still find what is in them. A mod that keeps its progress in a set or dictionary - rooms visited, berries collected, doors opened - stores a lookup number for each entry at the moment the entry goes in, and those numbers are made fresh every time the game starts. Akron wrote the old run's numbers into the saved state and put them straight back on load, so the collection came back holding everything it should, counting right and listing right, while asking it whether it contains any one of its own entries answered no. The mod then ran as if you had never been to the room, and nothing said so: the load reported success. Akron now works those numbers out again from the entries themselves at the end of a load and leaves every entry exactly where the saved state put it, so the collection lists in the same order it did before and a later entry still lands in the same place. This covers sets, dictionaries and thread-safe dictionaries, whether they follow a language's alphabet, ignore case that language's way, use a comparison a mod wrote itself, or are keyed by an object rather than by text; collections that match text exactly were always fine, and sorted collections were never affected. It matters now because the fix above is what lets the language-aware ones into a saved state at all, but the same fault was already reachable by a mod that does its own hashing. Two older collection types are still affected and are not fixed here, `Hashtable` and `StringDictionary`; nothing in Celeste, Everest or Akron is known to use either, and neither can be repaired without giving up part of the check Akron runs over a finished load. Measured in the test suite with a real two-run round trip through a file. This has not been checked in game.
- Keep a StartPos set while a chapter's dialogue or cutscene artwork is in play. Celeste's dialogue box takes its frame from the portrait artwork the game loads once at startup, so any StartPos set while a dialogue box exists holds a reference to that artwork. Akron rebuilds a slot on top of a freshly loaded room, and a room that loaded with no dialogue on screen holds no reference to the portrait artwork at all, so Akron refused the slot's restart copy over it and emptied the slot, with `fresh resource key is unavailable: Monocle.Atlas` and the artwork's folder as the reason. Akron now takes artwork the game has loaded from the running game rather than looking for it in the room, which is what it already does for textures and for mod assets. The window this closes is wider than it sounds. Celeste sets the handler that skips a cutscene when the cutscene starts and never clears it, so it goes on pointing at that cutscene, and its dialogue box, for as long as you stay in the chapter. On a chapter that opens with a cutscene, every slot set anywhere on the map was refused for the rest of the visit. Artwork a mod loads and keeps to itself is still refused, and the refusal names the folder it came from so you can tell which mod it is. Measured in the test suite. This has not been checked in game.
- Clear the room's trails before rebuilding it for a StartPos load, the way Celeste's own room reload does. Celeste draws a fading trail behind anything that dashes or flies: the player, Seekers, Oshiro, the Badeline boosts, the birds, the final boss, and the demonstration ghost some rooms place to show a move. Those trails are drawn from the entity's own sprite rather than from a copy of it, so a trail still on screen is still holding the thing it came from. Rebuilding the room for a load destroyed those entities and left the trails holding them, and the room load then built fresh copies carrying the same map identity, so the rebuilt room contained two of everything that had been trailing, one of each dead. Akron matched the saved room against a dead copy and refused the load, naming the object it could not place. Celeste clears the trails first every time it reloads a room, which is what dying does, so its own rebuilt rooms never look like this and only Akron's did. This is the reason a slot set in a room with a demonstration ghost refused to load; the same failure was reachable for every other trailing object, including your own dash trail if you set the slot mid-dash. Akron now clears them in the same place Celeste does, so those slots load. Trails left over from before the load no longer flash up for an instant afterwards either. This has not been checked in game.

- Leave the room's playback ghost out of a StartPos saved state, so slots set in rooms that have one can be loaded. The ghost is the demonstration figure some rooms place to show a move, Celeste's `playbackTutorial` entity, and it is the one room object Akron has never been able to match up between the room you saved and the freshly loaded copy it rebuilds on. Any slot set in a room with one refused to load, naming `Celeste.PlayerPlayback`. Akron now leaves the ghost and the trail it draws out of the saved state and lets the freshly loaded room keep its own. What you give up is where the ghost is in its loop: after a load it is where a clean room load puts it, about a second before its demonstration starts, rather than where it was when you set the slot, and its footsteps and dash sounds restart with it. If you are practising to the ghost's timing, that is the thing to know. Nothing else in the room changes - the ghost has a hitbox but nothing in Celeste ever checks against it, nothing tracks it, and no code reads it. This only covers the ghosts a map places in a room; the playback figures a cutscene owns, such as Farewell's wave-dash tutorial, are saved and restored exactly as before. This has not been checked in game.

- Load a StartPos set during the first seconds of a modded chapter. Modded maps open with Madeline's wake-up animation, and the coroutine running it holds one mid-flight animation object in three places at the same time. Akron rebuilt the first of the three and refused the other two, so a slot set in roughly the first two seconds of entering a chapter was set and written to disk normally and then could never be loaded - you only found out later, when you needed it. Measured on the test machine: ten slots on Midnight Aquarium and ten on Spring Collab 2020, nineteen of which hold that object, now all load after a full restart of the game, and every restored frame is identical pixel for pixel to the frame that was on screen when the slot was set. On a build without this fix the nine Midnight Aquarium files that hold it are all refused. Slots set later in a room are unaffected either way.
- Load a StartPos when the room holds one callback object in two places. Celeste and its mods routinely put a single handler in more than one slot: every tween in a room shares one easing function, and a mod that hands the same method to a one-shot timer and to a state callback shares one object between them. Akron's index of the freshly loaded room recorded which slot runs that handler only for the first place it found it, so a saved room that reached the same handler through the second place could be refused for a callback the room in front of you was running. A second condition is needed as well: the array holding the callback has to have changed length since the slot was set, which is what stops Akron matching the slot up directly. This one was found by reading the code rather than in play - no slot on the test machine has hit it - and the loads that are refused for a real reason are unchanged, including a slot set with a mod's variant switched on and loaded with it switched off.
- Refuse a StartPos whose saved state machine slots no longer mean what they meant, instead of loading it wrong and saying nothing. Celeste numbers an entity's states, and a mod that adds a state gets whatever number is free at the moment it adds it, so installing, removing or updating a mod can move a state from one number to another between the day you set a slot and the day you load it. Akron matched saved callbacks by that number, so after such a change it put a mod's callback on a different state, left the state that should have had it empty, and reported the load as a success - a room that looks right and behaves wrong, with nothing in the log. Celeste and Everest also record a name for each state, so Akron now compares the name the slot saved for a state against the name the freshly loaded room has for it and refuses the load, naming the state, when they disagree. If your mods have not changed since you set the slot, nothing changes for you: both runs give the states the same names, so nothing is compared unfavourably, and a mod that moves one of its own callbacks between states while you play, or adds a state while you play, still loads. The slots this newly refuses are ones where a mod has since been updated and renamed a state - Akron cannot tell a state that was renamed from a state number that now means something else, and the second of those is the wrong room this exists to prevent, so it refuses and tells you to set the slot again. What this does not cover is a state added by an older technique several mods still use, which grows the state machine without giving the new state a name - there is no name on either side to compare, so a shift among those states still loads the way it did before. XaphanHelper, BrokemiaHelper, JackalHelper, IsaGrabBag and PrismaticHelper are among the mods that use it; FrostHelper and CommunalHelper, the two most-installed helpers, name their states and are covered. One deliberate change: a mod that renames one of its own states while you play now has that slot refused rather than loaded. Every one of the 6,015 mods published for Everest was checked and none of them renames a state after the entity is built, so this should not be reachable, and a mod that starts doing it gets a refusal rather than a wrong room. Measured in the test suite. This has not been checked in game.
- Keep the automation command queue alive across a StartPos load. A load restores Celeste's frame counter along with the rest of the room, and the queue scheduled its next filesystem poll against the counter it had before the load, so loading a state captured earlier in the same run left the queue idle for the difference - three and a half minutes in one case measured on the test machine, with no error anywhere. This only affects the file-driven automation queue that the in-game verification harness uses; it is off unless `AKRON_AUTOMATION_ENABLED` is set.
- Stop a startup backup from deleting the backups you already had. StartPos writes one saved room state per slot into the Saves folder, so every backup swept those up with your save files and came out at 200 MB or more on the test machine. Four of those filled the 1024 MB total-size limit, and the fifth backup deleted everything the retention floor did not protect - real save backups, thrown away for a folder you never asked to have backed up. A backup now holds your save files and leaves `Saves/AkronStartPos` out, a restore leaves that folder alone instead of deleting it, and both the Backups panel and `_akron-backup.json` say so. You rebuild a StartPos saved state by setting the slot again, and setup packs already carry them between installs. The one thing this gives up is that restoring a backup no longer rewinds StartPos saved states with your save file; a slot you have re-set since the backup was taken still holds what you set last.
- Say the read-ahead cache is full instead of blaming the snapshot file. When the cache had less room left than the next slot needed, Akron started reading that slot anyway with the read cut off at whatever was left, which threw, and the run line reported it as a slot it "could not be read" - the wording for a corrupt file. It now works out how much the slot expands to before reading any of it, reports it as not fitting the remaining budget, and does not decompress up to a full budget's worth of data to find out.
- Stop backups from failing on Windows. Akron keeps `akron-current.log` open while the game runs, and Windows does not let a second reader open a file that is held open for writing unless that reader asks for it. The backup did not ask, so on Windows it hit its own log file and gave up on the whole archive, taking the rest of the Saves folder with it. Backups now read every file in a way Windows allows. This is not visible on Linux, which does not enforce any of this.
- Finish a backup that hits a file it cannot read instead of abandoning it. One locked or unreadable file used to fail the entire backup. The backup now archives everything else, leaves that file out, and names it both in the result line and in `_akron-backup.json` inside the ZIP, so a backup never quietly comes up short.
- Stop a restore on Windows from deleting every one of your save files and then failing. This is the serious one. Akron writes the graphics library it needs into `Saves/AkronNative` and loads it when it starts, on every platform, and Windows does not let anything delete a library that is loaded. Restore deleted your save files first, walked on into that folder, hit the library, gave up, and unpacked nothing - 18 save slots to none, in the real game, every time, for every Windows player. The only way back in game was Restore, which then did the same thing again. Akron now leaves its own folders alone: the graphics library, recordings that FFmpeg is still writing, an FFmpeg build you put in `Saves/AkronTools`, and the automation scratch files. Backups do not carry them either, so backups get smaller too. Nothing here is save data and nothing is lost by leaving it - the graphics library is written again on the next launch.

- Make a failed restore harmless. The reason the bug above cost people their save files rather than just failing is that restore deleted before it unpacked, so anything going wrong in between took everything with it. Restore now unpacks the backup into `Saves/AkronRestore` first, which puts the step most likely to fail - a bad ZIP, a full disk - before anything of yours is touched. Only then does it move your current files aside and the unpacked ones in, and if any of that will not go through it puts back everything it has already moved and stops with a message saying your save files were not changed. Your old files are discarded last, after the restore has already worked. So a file that something else on your machine holds open now turns a restore into a refusal instead of into lost saves. The one thing that does not stop a restore is a library the game already has loaded, because Windows allows a loaded library to be moved even though it refuses to delete one; those live in the Akron folders listed above, which a restore leaves alone by name. The Windows half of this is not visible on Linux, which renames and deletes open files without complaint, and it has not been checked in game.

- Stop a restore that worked from telling you it half failed. Restoring a backup that was taken with no save file open - which is what a backup made at the main menu records - ended on `Restored files, but live reload failed: no save slot could be determined`, even though every file had been restored correctly. After a restore Akron reloads the save file you have open, so the copy Celeste is holding in memory cannot be written back over the files just put on disk. At the main menu no save file is open, so there is nothing in memory to correct and nothing that could overwrite anything, and Akron was calling that a failure. It was also stopping there, before rebuilding the menu, which is the step that makes the file list read your restored files, so the one thing the message said had not happened really had not happened. A restore with no save file open now says it restored the backup and rebuilds the menu. Two smaller things go with it. Akron used to pick the file to reload by preferring the slot written into the backup and only then the file you have open, so restoring a backup taken on one profile while another was open moved you onto the backup's profile; it now reloads the file you have open, which is the only copy that can be stale. And the debug save file is no longer mistaken for no file at all: Celeste numbers that one -1, which is exactly what Akron was reading as nothing being open, so a restore with it open reported the same false failure instead of reloading it. The message survives for the case it was written for, a save file that is open that Akron cannot read back after the restore, and it now always names that file. In that case Akron also closes the save file you had open rather than leaving the old copy of it loaded, because that copy is the one thing left that could write the pre-restore data back over what the restore just put on disk; you come back to the main menu with no file open, which is where the game starts anyway. This has not been checked in game.

- Remove `Saves/AkronRestore` when a restore finishes. A restore unpacks into a folder of its own inside `Saves/AkronRestore` and deletes that folder when it is done, but never the `AkronRestore` folder around it, so every restore left an empty one behind. That folder is meant to tell you a restore was interrupted, and it had stopped telling you anything. Akron now removes it once the last piece of restore work inside it is gone, and leaves it exactly where it is while anything remains in it, so it is again a sign that a restore did not finish. Measured in the test suite.

- Stop a restore on Windows from failing after it has already deleted your save files. Restore deletes the Saves folder and unpacks the backup over it, and Windows will not delete or replace a file that something still has open, which the Akron log and an in-progress performance recording both did. Akron now lets go of both before it deletes anything.
- Refuse to restore when the safety backup taken first is missing anything. Restore makes a pre-restore backup and then deletes every save file, so a safety backup that could not read one of them was no safety net at all. Restore now stops and names the file instead.
- Stop a settings change from being able to freeze the game. Flipping a toggle in the overlay asked Everest to save every installed mod's settings and then waited for that to finish on the same thread the game runs on, which stops the game completely until the save reports back. If the save never reported back the game never came back either, and it had to be killed. Akron now writes its own settings file itself, on the spot, and touches nothing else.
- Never leave `modsettings-Akron.celeste` half-written. Settings used to be saved by deleting the file and writing a new one in its place, so a crash, a freeze or a power cut during the write left the file truncated and every Akron setting was gone. The new file is now written beside the old one and swapped in as one step, so the file on disk is always either the settings you had or the settings you just changed.
- Stop the restart copy of a StartPos from building its saved graph the slow way. It copied the whole path back to the root of the room for every field it looked at, so the work grew with the square of how deeply nested the room was, and none of those copies could reach the saved file. On a graph shaped like a real room this cut the memory the copy churns through by about 4x, and by 26x on the deepest chains measured.
- Stop the restart copy of a StartPos while you are playing. The copy runs in the background after you set a StartPos, and the memory it churns through is what causes the small collections that drop frames. It now waits for a pause, chapter select, a StartPos input wait or the end of the level and runs at full speed there. It writes the same file from the same inputs, so nothing about which StartPos loads changes.
- Stop a StartPos you just set from disappearing after you load a savestate. The background copy that makes a slot survive leaving the map checked whether it was still on the save file it started on by comparing the save data object rather than the save file. Loading a savestate replaces that object, so every copy still in progress reported that the save file had changed and every slot that had nothing saved before it was removed. It now compares the save file itself, and writes into whatever is holding that file at the time.
- Stop a savestate load from hiding the StartPos slots you set after taking it. A savestate rewinds gameplay, and it was rewinding Akron's list of StartPos slots with it, so a slot set after the savestate reported itself as empty and could not be loaded until the game was restarted, even though its saved state was still on disk. Worse, that rewound list was what the next save would have written. Akron now keeps the list across a savestate load and rebuilds it afterwards.
- Load a StartPos whose restart copy has not finished instead of refusing it. Coming back to a chapter used to report that the copy had not finished yet, with no way to act on it, and the copy could not finish while you were in the level. The load now finishes that one copy first, so it costs a few seconds rather than failing. If it still cannot finish, the message says to pause for a moment and try again.
- Stop quitting from hanging on the StartPos queue. Closing the game finishes the copies that are still outstanding, which took over twenty seconds with a full queue on the test machine, with nothing on screen to say why. Quitting now spends a few seconds on them and then stops; slots that did not finish are named in the log and have to be set again. A slot that was already saved is never affected.
- Report how long a StartPos restart copy actually worked for in the Akron log, instead of counting the time it spent waiting for you to stop playing. The waiting time is now a separate figure on the same line.
- Stop the **Diagnostic** logging level from also writing per-event Verbose lines, which filled the whole rotation budget in a couple of minutes of play.
- Write the Akron log through a single held-open file handle instead of reopening the file for every line, which takes about six blocking filesystem calls per line off the render thread.
- Read the other slots of the map in the background after re-entering a chapter as well, instead of only after a fresh launch. Re-entering a chapter leaves the slots loadable but no longer instant, and Akron was treating them as instant, so every Load on that map paid the full snapshot read again. Measured on the test machine: the second Load after a chapter round trip went from 4.1 s to 2.8 s.
- Say **cold** instead of **warm** in the StartPos load timing written to the Akron log when the load rebuilt from the snapshot. A load after re-entering a chapter was reported as warm while taking seconds.
- Restore StartPos slots across rooms and after leaving or restarting a map, including positions set during room wipes in large custom maps such as Heart of the Storm.
- Restore StartPos in rooms with groups of linked mod entities, such as Spring Collab 2020's Ancient Engine. Checked in game on that map: eight slots, one per room, including the three rooms carrying ten or eleven linked clutter blocks. After a full restart all eight loaded from disk and every restored frame was identical pixel for pixel to the frame that was on screen when the slot was set.
- Keep loading a StartPos from re-enabling **Respawn at StartPos** after the player turns it off.
- Stop a failed restart copy from leaving a StartPos slot that works for the rest of the session and then refuses to load after leaving the map. A slot that held no StartPos is now emptied as soon as its restart copy fails, and the message names the slot and the reason.
- Keep the StartPos a slot already held when setting over it fails. The previous position and its saved state stay in place and still load, and the message says which slot was not replaced and why.
- Write StartPos load and save diagnostics to the Akron log, so a bug report that attaches `akron-current.log` now contains the StartPos evidence. They only went to Celeste's `log.txt` before.
- Say why a StartPos load failed. Failures used to report a bare result code, and a load that was cancelled at the engine boundary or that failed and rolled back reported nothing at all, which looked exactly like a dead hotkey.

- Refuse a StartPos that would put a mod's extra player state on the wrong state number, instead of loading it and leaving the mod confused. Nine widely used helpers add their own player states the old way, by growing Celeste's four state arrays by hand and never writing the state's name, and XaphanHelper, BrokemiaHelper, JackalHelper, IsaGrabBag and PrismaticHelper are five of them. The state number a mod gets that way depends on the order the installed mods run in, so installing, removing or reordering a mod between setting a slot and loading it moves those states around. Akron checked the state names to see whether a saved state number still means the same state, and two unnamed states looked identical, so the load went ahead and renumbered the machine back to how the saving session had it while the mods in front of you kept using the numbers this session handed out. The load reported success. Akron now falls back to the code the state runs when neither side names it, so a slot whose states moved is refused with the state number in the message and nothing in the room is touched, and a slot whose mod set has not changed loads exactly as before. The one thing this gives up: if a mod changes one of its own unnamed states' update or coroutine while you play, that slot is refused too, because with no name there is nothing to tell that apart from the state having become another mod's. Rewiring a state's begin and end callbacks, including moving one between two states, is unaffected. Measured in the test suite against real Monocle state machines. This has not been checked in game.

- Refuse a StartPos that cannot find a mod's own type any more, instead of quietly handing the room a different type. Akron records a reference to a type, a mod, or a mod's settings object by name, and it used to treat those names as trustworthy only for code that came off disk as a file. Everest never loads a mod that way - it reads the mod's dll into memory so the file is not locked - so no mod's code ever qualified, and every reference to a mod's own type fell back to matching by position in the room instead. If the mod was gone when you loaded the slot, the position match handed the room whatever type happened to sit there and the load reported success. Akron now asks which loader Everest used, so a name that came off a mod's own dll counts as a name and a name a mod's code made up for itself at startup still does not. A slot that names a type this install no longer has is refused, with the name in the message. Nothing changes while the mod is installed. Measured in the test suite through Everest's real mod loader. This has not been checked in game.

## Akron Beta 67

### Added

- Add a **Wait for input after load** StartPos option that pauses gameplay until a fresh input, while backdrops and respawn wipes keep moving.

## Akron Beta 66

### Changed

- Recreate existing StartPos slots and setup packs for the v7 snapshot and v4 setup contracts.

### Fixed

- Restore berry collection progress when loading a StartPos, including after restarting Celeste.
- Keep each chapter's StartPos slots usable after switching chapters and setting the same slots elsewhere.

## Akron Beta 65

### Changed

- Keep StartPos Set and same-session Load on the fast in-memory path, then cache the first disk restore so later Loads stay fast.

### Fixed

- Restore stopped custom-map sounds from StartPos disk snapshots and keep later live sound changes current for subsequent Sets.
- Prevent replaced StartPos data, failed room baselines, and queued shutdown work from leaving stale or incomplete restart copies.
- Keep StartPos setup imports consistent when they replace a still-saving slot or a helper mod fails its cleanup callback.
- Block StartPos setup exports and uploads until every included restart copy has finished saving.
- Keep unfinished StartPos restart copies bound to the save file that created them.
- Reuse the true fresh-room baseline when a new StartPos follows a warm cross-room Load.
- Fall back to the restart-safe StartPos after leaving and re-entering a chapter invalidates its warm copy.
- Refresh the fresh-room baseline after normal reloads so StartPos uses current save progression.
- Match warm and restart room initialization without pausing Akron input for the full entry wipe.
- Keep permanent save progress and other mods' save data current during warm StartPos Loads.
- Keep helper-owned sounds dormant and releasable while Akron holds a StartPos snapshot or fresh-room baseline.
- Release replaced StartPos graphics after their background restart copy finishes without dropping graphics still owned by another slot.
- Keep failed or unfinished StartPos replacements from loading an older disk snapshot, and reject stale warm copies before helper load callbacks run.

## Akron Beta 64

### Changed

- Store dependency-free StartPos v6 snapshots on disk so exact room state survives closing and restarting Celeste, including active custom-map gameplay.
- Carry each exact StartPos v6 room snapshot inside setup and community packs instead of exporting coordinates alone.
- Use the `akron-setup-v3` pack contract. Akron rejects older `akron-setup-v2` local and community packs instead of importing partial room state.

### Fixed

- Restore the exact StartPos frame both in-process and after a full restart, including room objects, positions, speeds, registered helper state, audio, random state, and gameplay render buffers, before simulation continues.
- Keep later StartPos slots available after loading an earlier slot, including custom-map runtime effects and generated room state.
- Keep global save progress with the active file and bind imported StartPos snapshots to the recipient's save slot.
- Show the actual death position when no hazard overlaps the player instead of choosing the nearest spike or death zone.

## Akron Beta 63

### Fixed

- Show the seeker collision that killed the player instead of a nearby spinner in Show Hitboxes On Death.

## Akron Beta 62

### Fixed

- End numeric-field editing when the overlay closes so fields reopen unfocused and held gameplay keys are not typed into them.

## Akron Beta 61

### Fixed

- Keep native spinner and spike geometry in death hitboxes unless Fix Hitbox Pixels is enabled.
- Keep Everest's debug console closed while typing `.` in an Akron text field.
- Stop the Backups panel from reopening every backup ZIP on each rendered frame.

## Akron Beta 60

### Fixed

- Open the normal binding menu by right-clicking an actionable popup option instead of showing a separate binding list.
- Keep Everest's debug console closed when `.` is being captured or already belongs to an Akron binding.
- Keep every overlapping part of a spinner hitbox visible after death when All hitboxes is off.

## Akron Beta 59

### Added

- Bind actions inside option popups, including Frame Stepper's Step Once action.

### Fixed

- Give Akron temporary ownership of the period key when an Akron action uses it, so Everest does not also toggle debug hitboxes.
- Recenter positional sound effects on the restored StartPos room.
- Show Auto Kill areas with death hitboxes even when live area display is off.
- Preserve spinner collider shapes when only the death-object hitbox is shown.
- Draw Show Triggers outlines at 5 pixels.

## Akron Beta 58

### Added

- Route music and sound effects to separate output devices with Audio Splitter.

### Changed

- Group overlay features by task, with Frame Stepper under Global controls and Control Display under Player HUD controls.
- Keep the configured Timescale value separate from whether Timescale is enabled.
- Keep the configured Transition Speed value separate from whether Transition Speed is enabled.
- Clarify the Dream State and Golden Start tooltips.

### Fixed

- Keep the stable website download fallback synced to the latest GameBanana file after releases.
- Restore tooltip and search copy for Previous Room In Order and Next Room In Order.
- Keep Audio Splitter enabled while Celeste's audio system and music bus finish loading.
- Keep dropdowns and color pickers open when clicked more than once inside an options submenu.
- Show only the recorded death-object hitbox when All hitboxes is off.
- Gray out Timescale and Frame Stepper when no save session is active.
- Give submenu labels more room before shortening them with an ellipsis.

## Akron Beta 57

### Changed

- Use the configured overlay accent color for every enabled toggle, including multi-mode options.

### Fixed

- Prevent duplicate Auto Kill condition controls from triggering Dear ImGui ID conflicts.
- Limit Deload Spinners to one simulation per level to prevent repeated-use crashes.
- Simplify Show Hitboxes options and use a 5-pixel default trajectory thickness.

## Akron Beta 56

### Fixed

- Keep Refill Clarity within the refill's original sprite bounds while preserving Celeste's black outline.

## Akron Beta 55

### Changed

- License Akron-owned material under CC BY-NC-ND 4.0 and include the license and complete third-party notices in player packages.

### Fixed

- Keep Refill Clarity outlines complete for atlas-trimmed and custom refill sprites.

## Akron Beta 54

### Fixed

- Make captured overlay bindings activate immediately for every actionable option.
- Keep death hitboxes correctly aligned while the screen wipe covers them.

## Akron Beta 53

### Added

- Enlarge Community Pack previews in an in-game lightbox and open a pack's source Discord thread from its detail pane.

## Akron Beta 52

### Fixed

- Show Community Pack previews and Discord author avatars, close the Community Packs window with the Akron overlay, and report import progress and completion.

## Akron Beta 51

### Fixed

- Keep custom Control Display boards valid when importing or editing them, and avoid rebuilding the board every frame.

## Akron Beta 50

### Fixed

- Add Lag Pauser recovery grace after respawns and room transitions, plus a repeat cooldown after automatic pauses.

## Akron Beta 49

### Security

- Harden Community Pack archives, catalogs, previews, uploads, and setup imports against malformed or oversized input.
- Bound screenshot, map-stitching, automation, network, and local-file resource use.
- Add verified release artifacts with checksums, an SBOM, and provenance attestations.

### Fixed

- Preserve machine-local Auto Deafen bindings and StartPos slots belonging to other maps when importing shared setups.
- Keep imported StartPos room-state snapshots immediately available after import.
- Make Community Pack uploads and catalog publication recover safely from interrupted or concurrent operations.

## Akron Beta 48

### Fixed

- Keep StartPos deaths on Celeste's normal death animation and wipe before restoring across room changes, stop saved positional sounds from lingering after room transitions, and make Transition Speed values below 1x actually slow transitions.

## Akron Beta 47

### Fixed

- Use localized map names, such as Forsaken City, in generated Upload Pack titles.
- Show every submitted image in Discord moderator reviews and published galleries, with StartPos uploads beginning at Slot 1 and gallery navigation and download controls.

## Akron Beta 46

### Fixed

- Let Upload Pack submit its generated marked-room capture payload, keep edited upload text scoped to the current map, and make overlay binding capture update Celeste's native key bindings.

## Akron Beta 45

### Added

- Add an Only Marked Rooms map-capture option and use marked-room previews for Community Pack uploads, with multiple catalog images shown as an in-game carousel.

## Akron Beta 44

### Added

- Let StartPos `.akr` and Community Pack uploads include portable room-state snapshots while stripping deaths, time, and other stats from imported room-state restores.

## Akron Beta 43

### Fixed

- Show at least 15 StartPos slots in the selector by default and arm StartPos death reloads after a successful StartPos load.

## Akron Beta 42

### Fixed

- Keep StartPos slots scoped per map and restore persisted StartPos player/session state across game restarts without rewinding time or deaths.

## Akron Beta 41

### Added

- Let the in-game Community Packs upload flow submit Auto Kill and Auto Deafen area packs, with generated metadata shown directly in the upload form.

### Changed

- Simplify the Upload Pack popup with aligned fields, generated text shown in editable fields, and no separate preview block.
- Keep Upload Pack feedback visible with a compact progress bar while Akron captures the full map and uploads the submission.

### Fixed

- Show the selected Upload Pack markers in automatic full-map captures, even when normal scanner marker export options are off.
- Replace raw Upload Pack completion states with a Discord confirmation/review prompt and theme the upload progress bar with the active Akron accent color.
- Show Upload Pack server failures in the popup and stop before full-map capture when the upload endpoint is unavailable.

## Akron Beta 40

### Added

- Add the in-game Community Packs upload flow for StartPos packs, including automatic full-map capture, generated metadata, saved attribution, and Discord moderation handoff.

### Changed

- Document the Community Packs upload and publication architecture without tying the player-facing catalog contract to provider-specific free-tier details.

## Akron Beta 39

### Fixed

- Keep Lag Pauser from counting Celeste's native freeze frames and native StartPos restores as lag spikes.
- Add an opt-in Lag Pauser Ignore SRT option that skips Speedrun Tool load-state hitches for a brief grace window.

## Akron Beta 38

### Fixed

- Write merged room collages after Room Capture and world-space `map.png` outputs after Map Capture, with scanner markers drawn after stitching at one game pixel and a Downscale option for safer large map exports.

## Akron Beta 37

### Added

- Add an in-game Upload Pack popup for submitting Community Pack drafts with section, attribution, generated title, description, and terms controls.

### Fixed

- Keep StartPos slots scoped per map and restore persisted StartPos player/session state across game restarts without rewinding time or deaths.
- Let Skip Cutscene run Celeste's active cutscene skip callback instead of leaving the level stuck in a skipping cutscene state.
- Let Akron's internal recorder find host FFmpeg and its Linux libraries from inside the Steam Runtime sandbox.
- Prefer the most specific matching SFX volume group so broad sound fragments do not shadow narrower controls such as Ridge Wind.
- Keep Pause Countdown from subtracting level clock time twice while waiting after unpause.
- Tighten several verified overlay, HUD, input, backup, StartPos, and runtime helper paths found during the player-visible checklist pass.

## Akron Beta 36

### Changed

- Keep Refill Clarity sprites and dialog source assets inside Akron's source resources while preserving the released mod zip layout.

## Akron Beta 35

### Fixed

- Keep Refill Clarity on the Better Refill Gems single-sprite replacement path while still applying Akron's color and opacity settings.

## Akron Beta 34

### Fixed

- Let Refill Clarity use Better Refill Gems-style sprite replacement for one-use dash crystals while keeping its color and opacity controls live.
- Let Entity Inspector click cycling continue when the next click lands on a different pixel of the same target stack.

## Akron Beta 33

### Fixed

- Keep hitbox rendering aligned with Celeste's gameplay camera and live collider data.

## Akron Beta 32

### Fixed

- Persist overlay category collapse state across restarts.
- Keep Entity Inspector pin popups on-screen, make same-target click cycling close after the last hit, reduce duplicate collapsed details, align pin targeting during zoomed-out views, and remove extra rectangular highlights from collider-backed targets.
- Keep regular StartPos captures on the active slot after Set, so Load immediately returns to the captured position.
- Let imported and shared StartPos entries without runtime snapshots remain selectable and load as position-only starts.

## Akron Beta 31

### Fixed

- Keep the overlay responsive in mod-heavy setups by avoiding duplicated row filtering while external tool panels are placed.

## Akron Beta 30

### Added

- Add Death Particles customization for color mode, preset shapes, custom canvas masks, and particle duration.
- Let each Auto Kill area keep its own conditions, copy configured defaults into newly placed areas, and highlight the selected area brighter while its conditions are edited.

### Fixed

- Prevent submenu clicks from selecting or activating overlay rows behind the popup.

## Akron Beta 29

### Added

- Add Entity Inspector cursor pinning: hold the inspector cursor bind to click entities or triggers in-game, cycle overlapping hits, view runtime and source-bound map properties, and copy an inspection report.
- Add Entity Inspector close and hover-preview controls, highlight pinned and hovered targets, keep solid-tile highlights scoped to the hovered tile, and let Cursor Tools use Entity Inspector as its left-click action.
- Add cursor hold binding controls to the Click Teleport, Cursor Tools, and Cursor Zoom popups while keeping Left Alt as the default.

### Changed

- Update overlay row and popup classification labels to match the latest dcheat classification list, including Backups, Logging, Autosave, recorder, custom label, keybind, and gameplay-mutating utility classifications.

### Fixed

- Let Celeste's Journal shortcut take priority over Akron's default Tab overlay bind in the overworld.
- Keep Entity Inspector's submenu aligned with Akron's ImGui HUD style, require the cursor hold bind for gameplay pinning, add report placement/detail defaults, keep fixed report corners anchored across size changes, and keep titlebar-collapsed reports reopenable.

## Akron Beta 28

### Added

- Add Inspector Pin to Entity Inspector: click entities or triggers in-game, cycle overlapping hits, view runtime and source-bound map properties, and copy an inspection report.
- Credit viddie's Inspector Pin suggestion in the docs.

### Fixed

- Let Celeste's Journal shortcut take priority over Akron's default Tab overlay bind in the overworld.
- Keep Entity Inspector's submenu aligned with Akron's ImGui HUD style, make the row enter a visible pick mode, add report placement/detail defaults, keep fixed report corners anchored across size changes, and keep titlebar-collapsed reports reopenable.

## Akron Beta 27

### Added

- Add mouse control for Free Camera and an optional Cursor Tools hold bind with per-tool checkboxes for Click Teleport, Cursor Zoom, Free Camera, and Freeze gameplay. Cursor Tools mouse movement is enabled by default while its Free Camera option is active.

### Fixed

- Keep Madeline visible when Free Camera freezes gameplay or Cursor Tools enables Free Camera.
- Keep Cursor Tools from freezing gameplay unless its Freeze gameplay suboption is enabled, and allow its Click Teleport option to work without the normal Click Teleport hold bind.
- Keep Cursor Tools Click Teleport aligned while Cursor Zoom is active, including near clamped edge focus, and move Madeline through the normal movement path so her hair follows during frozen teleports.
- Keep Freeze Gameplay highlighted while active, keep Cursor Tools popup labels readable, prevent repeated click teleports from desyncing Madeline's hair animation, and keep Cursor Tools click teleports targeted at the clicked cursor position while Free Camera and Cursor Zoom are active.

## Akron Beta 26

### Added

- Add Akron invincibility mode with per-effect controls for bottomless rescue, crush collision changes, lava and ice pushback, and spike ground refills.

### Changed

- Add a Diagnostic logging level for playtesting, make it the default, and aggregate repeated policy checks and feature-use records so Trace remains available without losing useful logs so quickly.

### Fixed

- Fix Creator > Map Capture exports on Linux so room images render the map instead of solid black frames.
- Fix Creator > Map Capture scans stalling or under-rendering rooms after moving through a chapter.
- Skip non-playable filler rooms during Creator > Map Capture so custom maps can finish exporting.

## Akron Beta 25

### Fixed

- Show the full suboption name on hover when a popup label is shortened with ellipses.
- Restore Madeline's collider after room and map capture, including stopped scans, so capture cannot leave her hitbox enlarged or suppressed.

## Akron Beta 24

### Added

- Add directional Dash Redirect controls for preserving selected dash directions when Celeste would redirect them.
- Add Auto Kill area conditions for speed ranges, movement direction, current dash count, grounded/airborne state, and player state.
- Collapse Auto Kill's optional area conditions under a Conditions section so the popup stays focused on method and area selection by default.

### Fixed

- Add Extended Camera Dynamics external tool rows, route Cursor Zoom zoom-out through ECD when its hooks are active, and keep Akron from resetting ECD-owned zoom state when inactive.

## Akron Beta 23

### Fixed

- Keep overlay search responsive while playing a map by filtering on stable row labels and aliases instead of live row values.
- Hide backup folder paths in Backups > Last Result while Streamer Mode is enabled.
- Include the implicit Start checkpoint in Creator checkpoint navigation when a map has no checkpoint entity there.

## Akron Beta 22

### Fixed

- Rebuild FrostHelper spinner renderers after StartPos restores so stale cloned border images do not crash rendering.

## Akron Beta 21

### Fixed

- Detect bottom killboxes accurately for hazard contact and respawn behavior.
- Keep StartPos-restored audio state intact after loading saved positions.
- Suppress Akron render surfaces while SpeedrunTool owns state rendering.
- Repair Air Jumps option handling near hazard and edge cases.
- Persist player option changes correctly, including Frame Stepper and EVM-related policy behavior.

## Akron Beta 20

### Changed

- Remove the duplicate Restart Level shortcut so Reload Chapter is the single chapter restart action.
- Add Viridity to special thanks.

### Fixed

- Persist Akron overlay option changes when toggles, numeric inputs, selector dropdowns, or overlay close events update settings.
- Hide Celeste's bottom-right save/load icon when Hide Saving Icon is enabled.

## Akron Beta 19

### Fixed

- Render percent signs literally in tooltip descriptions, including Bloom Level's 0% and 100% text
- Keep Click Teleport from snapping the camera back when Free Camera is active
- Fully hide the saving icon when Hide Saving Icon is enabled or game-frame capture is active

## Akron Beta 18

### Removed

- Remove Akron profiles, rulesets, built-in preset modes, and related profile/ruleset archives, manifests, commands, and docs

### Fixed

- Keep the gameplay debug pass from running while gameplay is idle so the Akron overlay stays available outside active level play
- Keep auto kill, auto deafen, and other world-space area overlays aligned with gameplay positions
- Preserve FrostHelper persisted state when restoring StartPos or savestates
- Skip non-restorable static members during native state restore
- Persist modified Akron profile settings across game restarts

## Akron Beta 17

### Fixed

- Prevent Deload Spinners from adding simulated frames to level, session, or journal time.
- Make Deload Spinners a one-shot action so stale settings and setup imports cannot replay the simulation after restart.
- Keep Mintlify's generated Open Graph previews working by using an SVG docs logo asset instead of the PNG logo path that produced empty generated preview images.

## Akron Beta 16

### Fixed

- Keep the Akron overlay visible during death-related flows.
- Show Logging as a true On/Off toggle in the overlay.
- Fix zoom drift for auto kill, auto deafen, and hitbox overlays.
- Reload the latest loaded StartPos on death and preserve active runtime audio/state.

## Akron Beta 15

### Added

- Add Symbiote, Carbon, Retro, Coniferous, and Wine overlay theme presets.

### Changed

- Let Mintlify generate page-specific Open Graph previews for the docs site instead of using one static logo image.

### Fixed

- Prevent active Control Display key editor fields from copying into another key when selecting a different key before blurring the input.
- Prevent Akron's overlay hotkey from opening over Everest's Enable or Disable Mods menu, where Tab favorites or unfavorites mods.

## Akron Beta 14

### Added

- Add local Akron diagnostic logging under the Interface tab, including log level, warning mirroring, file rotation, retained files, and a test entry action.

### Changed

- Reclassify Motion Smoothing FPS Bypass as regular clean while keeping TPS Bypass, object interpolation, TAS mode, and Nasty mode marked as Cheat.

### Fixed

- Keep the overlay Search textbox focused while backspacing from narrow queries into broader result sets.

## Akron Beta 13

### Fixed

- Let the Open Menu key cancel hidden Auto Kill and Auto Deafen area selection and reopen Akron, so players are not stuck in a frozen selection mode.
- Restore Akron-managed cursor visibility after StartPos placement, Auto Kill area selection, and Auto Deafen area selection ends.

## Akron Beta 12

### Added

- Add a Backups overlay tab for managing Akron save backups from inside the game.
- Add manual backup creation for the current Celeste `Saves` folder.
- Add automatic backup triggers for Akron launch, Akron close, save/settings writes, chapter entry, and timed intervals.
- Add a restore browser for backup ZIPs, including backup timestamps, file names, sizes, reasons, save slots, and pinned state.
- Add ZIP metadata in `_akron-backup.json` with the backup reason, creation time, Celeste version, Akron version, save slot, profile name, current area/room when available, and enabled Everest modules.
- Add backup pinning through sidecar `.pin` files so important backups are protected from automatic cleanup.
- Add retention cleanup by maximum count, maximum age, maximum total folder size, and protected newest backups.
- Add a `Last Result` popup with the latest backup status, last backup age, backup folder path, manual-create action, and open-folder action.
- Add user-facing docs for backup creation, restore behavior, metadata, pinning, and retention rules.
- Add feature tooltips for overlay actions.

### Changed

- Group automatic backup triggers into a `Triggers` submenu so the Backups tab stays compact.
- Make restore create a `pre-restore` safety backup before extracting the selected ZIP.
- Make restore reload the restored save data and return to the main menu so Celeste does not keep using stale in-level save state.
- Exclude `Saves/AkronBackups` from future backup ZIPs so backups do not recursively include older backups.
- Rename `Skip Cutscene / Dialogue` to `Skip Cutscene`.
- Verify the Backups overlay and manual backup creation in a live Celeste/Everest session, including ZIP readability and metadata contents.

### Fixed

- Prevent save/load restore crashes when a modded runtime rejects readonly field writes during deep clone.
- Prevent FrostHelper and other gameplay renderers from being interrupted by Akron overlay rendering.
- Preserve graphics device state after Akron draws ImGui overlay content.

## Akron Beta 11

- Add Spawn Jelly, Spawn Theo, Set Inventory, Dream State, and Core Mode overlay actions.
- Add Previous Map, Next Map, Previous Checkpoint, and Next Checkpoint creator navigation actions.
- Put Spawn Jelly and Spawn Theo in Shortcuts as regular action buttons instead of triangle option rows.
- Add Set Inventory dash and jump configuration, setup persistence, console controls, and optional death restore behavior.
- Make Dream State toggle Madeline's dream dash inventory state directly from the Player tab.
- Make Core Mode configurable as Hot or Cold, add Toggle/Cycle click behavior, and restore the level's original mode when a toggle is turned off.
- Add console controls and status output for Set Inventory and Core Mode.
- Simplify Akron's Mod Options menu so the in-game overlay carries the detailed feature controls.
- Group Creator navigation actions more predictably and rename in-order room warps to Previous Room In Order and Next Room In Order.
- Show Auto Kill and Auto Deafen area selection previews while placing areas, including a single-pixel marker before the first corner is set.
- Improve HUD/overlay scaling so rows, popups, resource bars, and area markers stay aligned across viewport scales.
- Render dash and speed numbers above Madeline instead of centered on her body.
- Add 1px submenu outlines and stop triangle hover targets from showing duplicate info tooltips.
- Keep ImGui popup positions and value rows stable when overlay scale changes.
- Add player-hurtbox hitbox filter and color controls to commands and UI.
- Update hitbox default colors to follow CelesteTAS conventions where available.
- Draw Madeline's hazard hurtbox with CelesteTAS-compatible bounds and pixel rounding.
- Classify spinner-style hazards for hitboxes and trajectory collision checks.
- Hide unknown collidable helper entities from the live hitbox overlay when Akron cannot classify them confidently.
- Keep hitbox lines at least 1 screen pixel thick so persisted thin settings remain visible.
- Move practice area pixel marker labels below the marked edge.
- Update README install buttons and public docs to use Akron's stable install endpoints.
- Document release configuration/runbook details for GitHub, GameBanana, README, and website release sync.
- Credit viddie's Spawn Jelly, Set Inventory, Dream State, and Core Mode suggestions in the docs.

## Akron Beta 10

- Render hover help popups on ImGui's tooltip layer so they stay visible above overlay rows.

## Akron Beta 9

- Keep legacy shortcut bindings from reappearing after startup normalization.
- Preserve right-side modifier keys when capturing menu bindings, including modifier-only binds such as `RightAlt` and `RightShift`.
- Keep Open Menu defaulted to `Tab` while allowing users to rebind it to another valid key.

## Akron Beta 8

- Keep only Open Menu, Click Teleport Cursor, and Cursor Zoom Hold bound by default.
- Keep Open Menu user-customizable while restoring Tab only for missing or empty menu bindings.

## Akron Beta 7

- Keep Tab opening Akron's menu even when stale or custom menu bindings no longer include Tab.

## Akron Beta 6

- Group the Sound tab's per-sound volume rows under collapsed Player, Objects, Entities, Ambience, and UI headers.
- Keep core Sound controls visible above the new groups.
- Reveal relevant Sound groups and children while searching, including group-name and individual sound matches.

## Akron Beta 5

- Test the automated release path with GameBanana API authentication before the upload form.

## Akron Beta 4

- Test the automated release path after tightening GameBanana login field selection.

## Akron Beta 3

- Test the automated release path after cleaning release-conflict artifacts.

## Akron Beta 2

- Test the automated release path after switching GameBanana publishing to the direct edit form.

## Akron Beta 1

- Test the automated release path for GitHub, GameBanana, README links, and website links.

## Akron Beta

- Add GitHub community templates for issues and pull requests.
- Document the repository formatting command for contributors.
- Make CI fail when the Celeste reference archive secret is not configured.

## 0.1.1-beta.3

- Add hidden showcase marker logs for OBS-synced feature demo recordings.
- Log ImGui top-level feature toggle intervals separately from popup detail changes.

## 0.1.1-beta.2

- Harden room and map capture exports.

## 0.1.1-beta.1

- Current beta version declared in `everest.yaml`.
- Public docs cover installation, first run, overlay use, feature policy, `.akr` archives, troubleshooting, and contributor workflow.

"""W-Spec-1 additions to HAND-AUTHORED dialogues, Act II NPCs (see _s1_hand.py for the rules and the Act I table).
Applied by tools/campaign/extend_hand.py. Not discovered by the registry (leading underscore).
"""

from tools.campaign.extend_hand import Extension
from tools.campaign.model import C, E, Node, go, has_flag, leave
from tools.campaign.specs._s1 import give, has_item, rep, take
from tools.campaign.specs._s1_hand import ROOT, active, at, root_choice, shield

# ---------------------------------------------------------------------------------------------------
# Frostfang Reach
# ---------------------------------------------------------------------------------------------------

CLAN_CHIEF = Extension(
    file="ClanChief.tres", dialogue_id="dialogue.clan_chief",
    variants=[
        shield("flag.main.storm_tyrant_done"),
        at("flag.beat.stormend_hjalvar", "cq_st_after"),
        shield("flag.beat.succession_decided"),
        at("flag.beat.hold_held", "cq_su_hjalvar"),
        shield("flag.beat.hjalvar_briefed"),
        active("quest.main.closed_hold", "cq_ch_hook"),
    ],
    nodes=[
        Node("cq_ch_hook",
             "'Southerner. You walked in through the cold, so you are either useful or lost, and the gate does not care "
             "which this month.' Hjalvar does not rise. 'Three cairns stand on the north ridge: the south one, the middle "
             "one in the pass, the one on the glacier. Every Stormbound child learns to read them before they learn their "
             "letters. All three went dark in the same moon, and since then weather has been coming down the passes "
             "without warning, in the shape of things that are not weather.' He glances at your hands and the ember in "
             "them. 'You carry what the Tyrant carries. I will not pretend that does not matter to me. Sigrun has oil and "
             "flint and a tally. Relight the cairns and be back before the storm answers, or do not come back. Either "
             "way I will know by the light.'",
             [go("Something else.", ROOT, tag="else"), leave("I will relight them.", tag="go")]),
        Node("cq_su_hjalvar",
             "Hjalvar sits on the bench by the moot stone with his arm bound to his chest and a bruise spreading under the "
             "bandage. The hold smells of singed fur and tallow. 'A mote came through the wall of shields and I did not "
             "duck. Sixty years of being right about weather, and I did not duck.' His good hand finds the stone. 'The law "
             "is plain: a chief who cannot lead into the storm names who will, or the clans stay home. I would name no "
             "one. I would keep the passes shut and let him spend himself on the wall, and in a hundred years the weather "
             "would be only weather. The young do not have a hundred years, and nor do you.' He lets out a breath. 'Hear "
             "the exile. I would not, but the clans insist. Then come to the stone.'",
             [go("Something else.", ROOT, tag="else"), leave("I will hear him.", tag="go")]),
        Node("cq_st_after",
             "Hjalvar is on the bench by the hearth with his shoulder bound, and the hold is quiet in a way it has not been in "
             "his lifetime. 'The cairns burn. The sky has stopped.' He says nothing for a moment. 'My great-grandmother's "
             "mother said a Stormbound man went up to the Celestial stair and came home to find the hold on fire. We do not "
             "say his name. We say the storm. I have told the young it was weather for sixty years, and you went up there "
             "and ended weather.' His voice cracks and he turns it into a cough. 'The hearth is yours, Seventh. Not as "
             "chief. As kin. Mind the Crossway on the road south: the Watch takes twenty-five gold from anyone coming "
             "down from here.'",
             [leave("Eat at his hearth.", tag="eat", do=rep("faction.frostfang_clans", 10))]),
        Node("cq_seat_hjalvar",
             "'Eight clans still sit at my fire and none of them has asked me to step down. A chief who cannot climb is a "
             "chief who stays. I have found that is a better chief than the other kind.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_seat_halvar",
             "'He sits the high seat now, the exile, and the clans bring him their quarrels with their eyes on the floor. I "
             "sit by the fire and I am not sorry. A hold that cannot change is a hold that dies slowly. I told him so once, "
             "and I was right and wrong in the same breath.'",
             [go("Something else.", ROOT, tag="else")]),
    ],
    node_choices=[
        root_choice("root", "cq_seat_hjalvar", "About the chief's seat.", "cq_seat_hjalvar", when=has_flag("flag.fork.succession_hjalvar")),
        root_choice("root", "cq_seat_halvar", "About the chief's seat.", "cq_seat_halvar", when=has_flag("flag.fork.succession_halvar")),
        # A failed Defend (the player fell) is taken up again here; see the Elder's restart_smoke.
        root_choice("root", "restart_hold", "The storm broke the line with me in it. I will hold the hold again.", "",
                    when=(C.QUEST_AVAILABLE, "quest.main.closed_hold"), when2=has_flag("flag.main.iron_king_done"),
                    do=(E.START_QUEST, "quest.main.closed_hold")),
        root_choice("root", "restart_succession", "I fell on the Stormcrown road. I will hold it again.", "",
                    when=(C.QUEST_AVAILABLE, "quest.main.succession"), when2=has_flag("flag.main.closed_hold_done"),
                    do=(E.START_QUEST, "quest.main.succession")),
    ])

CLAN_EXILE = Extension(
    file="ClanExile.tres", dialogue_id="dialogue.clan_exile",
    variants=[
        shield("flag.main.storm_tyrant_done"),
        at("flag.beat.stormend_halvar", "cq_st_after"),
        shield("flag.beat.succession_decided"),
        at("flag.beat.hjalvar_heard", "cq_su_halvar"),
    ],
    nodes=[
        Node("cq_su_halvar",
             "Halvar does not get up from his fire, and the fire is still too far from the hold's to be an accident. 'He has "
             "told you to be careful of me. Good. I would tell you the same. He calls the storm weather. It is a man, ours "
             "once, who went up a stair and came home to a burned hold, and I will not call him weather to spare anyone's "
             "feelings.' He turns the stump to the flames. 'The winches at Stormfall. They are older than the hold. The first Stormbound raised the Stormcrown by them and then, when "
             "the Tyrant took it, cut the cables to keep him in. I found a crew who would splice them. The Syndicate paid "
             "for rope. I paid in whatever Hjalvar left me, which was one hand. Three hours up, if you have the nerve, and "
             "the clans will not climb behind us because they will not follow a man who has been judged. But they will "
             "not stop us. That is the whole of my plan.' He almost smiles. 'It is a better plan than a wall. It is also "
             "a worse one. Choose.'",
             [go("Something else.", ROOT, tag="else"), leave("I will come to the stone.", tag="go")]),
        Node("cq_st_after",
             "Halvar sits on the moot stone, which no exile has touched in twenty years, his stump on his knee. 'Quiet.' He "
             "tastes the word. 'I thought it would sound like winning.' He turns his one hand over. 'The clans have asked "
             "me to sit the high seat. I told them I would think. I am thinking. Hjalvar has not spoken to me, but he has "
             "sent bread, and among the Stormbound that is a sentence.' He looks up. 'You did a thing nobody else could. I "
             "will not forget whose hand it was, even when I would like to. Keep some of that coin for the Crossway: "
             "twenty-five gold, going south.'",
             [leave("Take the clans' thanks.", tag="take", do=give("item.currency.gold", 200))]),
        Node("cq_seat_hjalvar_exile",
             "'My fire is still outside the hold's, and it is still the wrong distance. But I was not judged twice. The old "
             "fool kept his word, and I find I resent it.'",
             [go("Something else.", ROOT, tag="else")]),
    ],
    node_choices=[
        root_choice("root", "cq_seat_hjalvar_exile", "How is your fire?", "cq_seat_hjalvar_exile", when=has_flag("flag.fork.succession_hjalvar")),
    ])

CLAN_QUARTERMASTER = Extension(
    file="ClanQuartermaster.tres", dialogue_id="dialogue.clan_quartermaster",
    variants=[shield("flag.main.closed_hold_done"), at("flag.beat.hjalvar_briefed", "cq_ch_kit")],
    nodes=[
        Node("cq_ch_kit",
             "Sigrun counts the jars on her shelf twice before she answers. 'Oil, four lamps' worth. Flint, a good one, and you will bring it back. "
             "If you do not bring it back I will count you instead.' She pushes a stoppered jar and a flint haft across the "
             "table. 'The cairns are on the north ridge. If you see a mitten, do not touch it. The scout who wore it is not "
             "going to want it back.' She looks at you properly for the first time. 'The hold will know if the cairns "
             "burn. We always do.'",
             [go("Something else.", ROOT, tag="else"), leave("I will bring it back.", tag="go")]),
    ])

# ---------------------------------------------------------------------------------------------------
# The Ashen Wilds
# ---------------------------------------------------------------------------------------------------

MAEVE = Extension(
    file="AshenHeadwoman.tres", dialogue_id="dialogue.ashen_headwoman",
    variants=[
        shield("flag.main.beast_lord_done"),
        at("flag.beat.beastend_slain", "cq_bl_after_slain"),
        at("flag.beat.beastend_calmed", "cq_bl_after_calmed"),
        at("flag.beast_lord_defeated", "after"),
        shield("flag.beat.hearth_raid"),
        active("quest.main.last_hearth", "cq_lh_hook"),
    ],
    nodes=[
        Node("cq_lh_hook",
             "Maeve Ashby is sitting on an upturned crate with an old bow across her knees, a good ten paces closer to the "
             "fence than anyone else in Last Hearth. 'Wolves,' she says, without looking round. 'The first winter they came "
             "to the fence in threes, sniffed, left. Last moon it was a dozen. Last night I counted thirty, standing in a "
             "ring, not growling, not eating. Just standing, as if somebody had told them to wait.' She finally looks at "
             "you. 'I have eleven people and one fence. The Hunters on the rise say the beasts are being called. I say "
             "whatever is calling them is about to say something else. They will come as soon as the light drops, and the "
             "light is dropping. If you mean to stand with us, stand now. I will not ask twice.'",
             [go("Something else.", ROOT, tag="else"), leave("I will hold the fence.", tag="go")]),
        Node("cq_bl_after_slain",
             "Maeve Ashby stands at the fence with no bow in her hands for the first time since you met her. 'The plateau is "
             "quiet. The wolves have stopped coming to the fence at all.' She stares at the ash where the herd stood. 'The "
             "Hunters came through at dawn and counted. The fence has not heard so many dying since the Breach. I am glad "
             "of the quiet. I would like to be glad of it without the smell.' She hands you a wrapped bundle of salted "
             "meat. 'It is not much. It is what the herd would have been.'",
             [leave("Take the bundle.", tag="take", do=give("item.food.field_ration", 3))]),
        Node("cq_bl_after_calmed",
             "Maeve Ashby is in the middle of the fence line, laughing at a dire wolf. It lies on its side in the ash twenty "
             "paces from a calf elk and neither of them has a care in the world. 'They came back,' she says. 'Three nights "
             "ago, the whole ragged herd, and they settled by the fence like cattle. The wolf came to the gate this morning "
             "and asked for nothing.' She wipes her eyes. 'I have not slept a night through in thirty years. I did last "
             "night. I do not know how to thank you for the gap where the fear was.' She presses a stoppered bottle into "
             "your hand: Ada's tincture. 'For the road.'",
             [leave("Take it.", tag="take", do=give("item.potion.health", 2))]),
        Node("cq_herd_slain_note",
             "'The wolves come to the fence in ones now. The Hunters' count, not mine. We eat well. I try not to ask what.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_herd_calmed_note",
             "'The herd winters by our fence. We named the calf. Do not tell the Hunters.'",
             [go("Something else.", ROOT, tag="else")]),
    ],
    node_choices=[
        root_choice("root", "cq_herd_slain_note", "How is the fence?", "cq_herd_slain_note", when=has_flag("flag.fork.herd_slain")),
        root_choice("root", "cq_herd_calmed_note", "How is the fence?", "cq_herd_calmed_note", when=has_flag("flag.fork.herd_calmed")),
        root_choice("root", "restart_fence", "The fence broke when I did. Let me hold it again.", "",
                    when=(C.QUEST_AVAILABLE, "quest.main.last_hearth"), when2=has_flag("flag.main.iron_king_done"),
                    do=(E.START_QUEST, "quest.main.last_hearth")),
    ])

ADA = Extension(
    file="AshenMender.tres", dialogue_id="dialogue.ashen_mender",
    variants=[shield("flag.main.last_hearth_done"), at("flag.beat.hearth_held", "cq_lh_ada")],
    nodes=[
        Node("cq_lh_ada",
             "Ada Voss has wolf teeth in her apron pocket and blood to the elbows, and she does not stop winding the bandage. "
             "'You held the fence. Good. Now tell me what I am looking at.' She turns a man's arm to the light: grey skin, "
             "black veins, a bite that will not close. 'It is not the wolves. The wolves have always bitten. It is that the "
             "bites go grey in a day, and the ones who have them hear the beasts as if they were talking. The ash is calling "
             "them and it is calling my patients and I am out of things to burn.' She finally looks up. 'The Hunters on the "
             "rise keep a count. Go and see Hask Morrow. If you can spare three healing herbs on the way, I can make smoke "
             "that lets a bitten man sleep through the calling. Burned where the calling is loudest, it might quiet it for "
             "a while.' She almost smiles. 'If you cannot spare them, go anyway.'",
             [go("Here are three healing herbs.", "cq_lh_ada_herbs", tag="herbs", when=has_item("item.material.healing_herb", 3),
                 do=take("item.material.healing_herb", 3), do2=rep("faction.villagers", 5)),
              go("Something else.", ROOT, tag="else"),
              leave("I will find Hask.", tag="go")]),
        Node("cq_lh_ada_herbs",
             "She takes the herbs the way other people take bread. Within minutes a green-grey smoke is rolling off the "
             "brazier and the man with the grey arm is asleep with his face slack. 'I would tell you to be careful on the "
             "rise, but you have clearly decided what you are.' She presses a pair of health potions into your hand. 'For "
             "when the herbs run out.'",
             [leave("Thank her.", tag="go", do=give("item.potion.health", 2))]),
    ])

HASK = Extension(
    file="AshHunterWarden.tres", dialogue_id="dialogue.ash_hunter_warden",
    variants=[
        shield("flag.main.beast_lord_done"),
        shield("flag.rival.hearsay"),
        active("quest.main.beast_lord", "cq_bl_hearsay"),
        shield("flag.beat.hask_briefed"),
        active("quest.main.herd_and_hearth", "cq_hh_hook"),
    ],
    nodes=[
        Node("cq_hh_hook",
             "Hask does not take his eyes off the road. 'Forty-one maws past the rise since the thaw. That is the number the "
             "Huntmaster has. Here is the one she does not: wolf and elk and boar walking the same road at night, together, "
             "not hunting each other. I have staked three tracks. Read them for yourself, because I am a poor witness to my "
             "own count. The stakes are red. Then go where they meet and tell me whether I am mad.'",
             [go("Something else.", ROOT, tag="else"), leave("I will read the tracks.", tag="go")]),
        Node("cq_bl_hearsay",
             "Hask has not moved from the rail since you last spoke, but his glass is pointed a different way now: east along "
             "the plateau road, where the ash lies grey and untouched. 'Three nights before you came, a rider went up that "
             "road. Black iron, tall, no banner. The wolves parted for him like water. I have stood on this rise twelve "
             "years and they have never once made room for me.' He lowers the glass. 'He did not stop at the station, did "
             "not look at the herd. He stood at the foot of the ramp for the length of a count and then rode back down. I "
             "have been trying since to decide whether he was afraid of what is up there or sorry for it.' He looks at you. "
             "'He is a hunter, that one. I know the shape of it. I do not think the beast was his quarry.'",
             [go("Something else.", ROOT, tag="else"), leave("Thank him.", tag="go")],
             on_enter=(E.SET_FLAG, "flag.rival.hearsay")),
        Node("cq_herd_slain_hask",
             "'Forty-one dead on the shallow. The Huntmaster has the count. It is the cleanest cull this station has logged "
             "and I wish I had not been there to log it.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_herd_calmed_hask",
             "'Smoke.' He shakes his head slowly. 'Sixty years of the Hunters' manual and none of it says smoke. I have "
             "written it down, in the margin, in my own hand. If it works twice, they will make me put it in the book.'",
             [go("Something else.", ROOT, tag="else")]),
    ],
    node_choices=[
        root_choice("root", "cq_herd_slain_hask", "About the cull.", "cq_herd_slain_hask", when=has_flag("flag.fork.herd_slain")),
        root_choice("root", "cq_herd_calmed_hask", "About the smoke.", "cq_herd_calmed_hask", when=has_flag("flag.fork.herd_calmed")),
        root_choice("root", "restart_herd", "The smoke went out when I fell. I will go back to the herd.", "",
                    when=(C.QUEST_AVAILABLE, "quest.main.herd_and_hearth"), when2=has_flag("flag.main.last_hearth_done"),
                    do=(E.START_QUEST, "quest.main.herd_and_hearth")),
    ])

# ---------------------------------------------------------------------------------------------------
# The Sunspire Dominion
# ---------------------------------------------------------------------------------------------------

ODA = Extension(
    file="SunspireWellkeeper.tres", dialogue_id="dialogue.sunspire_wellkeeper",
    variants=[
        shield("flag.main.crimson_prophet_done"),
        at("flag.beat.prophetend_exposed", "cq_cp_after_exposed"),
        at("flag.beat.prophetend_turned", "cq_cp_after_turned"),
        at("flag.beat.prophetend_kin", "cq_cp_after_kin"),
        at("flag.crimson_prophet_defeated", "cq_cp_after_neutral"),
        shield("flag.beat.oda_briefed"),
        active("quest.main.dry_wells", "cq_dw_hook"),
    ],
    nodes=[
        Node("cq_dw_hook",
             "Mother Oda Sarn hands you a cup of water without asking and watches your face while you drink. It tastes of "
             "rain on a hot stone and, under that, of something burnt. 'There,' she says. 'You taste it. The Archive's "
             "three gauge stones are set into the oasis wall: east, middle, west. I have read them every dawn for forty "
             "years. All three went grey this season. My grandson walked south at midsummer with the Prophet's people and "
             "the water has been worse every week since.' She takes the cup back and pours what is left into the sand, "
             "which she has never done. 'Water is free. That is the oldest law in the Dominion and I will not break it by "
             "serving this. Read the stones for me. Then ask who is carrying jars through the gap.'",
             [go("Something else.", ROOT, tag="else"), leave("I will read them.", tag="go")]),
        Node("cq_cp_after_exposed",
             "Mother Oda Sarn stands on the lip of the cistern with her arms folded, looking down into water that is, at "
             "last, merely water. 'It tastes of rain on a hot stone,' she says. 'Only that.' Beside her a young man in a "
             "faded red robe sits on the stone edge with his boots in the sand, thin, coughing, not looking at either of "
             "you. 'My grandson came home at midwinter with a cough and an apology. The Archive's people read the stones "
             "with me now, every dawn, so I am not alone in looking.' She presses a flask into your hand. 'Spring water, "
             "from the first clean draw. It is not for drinking. It is for remembering there was a day it was not clean.'",
             [leave("Take the flask.", tag="take", do=give("item.potion.health", 2))]),
        Node("cq_cp_after_turned",
             "Mother Oda Sarn sits on the cistern rim with twenty former pilgrims in plain grey around her, passing a ladle. "
             "They look at you when you come in, and then at the water, as if checking whether you approve. 'They keep "
             "asking what to do next,' she says dryly. 'I told them to fetch water. They did. I told them to stop looking "
             "at me for instructions and they looked at you instead.' Her mouth thins. 'They are good people. They were "
             "told for years they were nothing, and then somebody told them they were something, and now they are waiting "
             "for the next somebody. I am not sure I like what you have made, and I am not sure I would have done better.'",
             [leave("Thank her.", tag="take", do=give("item.potion.health", 2))]),
        Node("cq_cp_after_kin",
             "Mother Oda Sarn keeps her eyes on the ledger at the cistern. The water in the cup beside her is "
             "still faintly grey. 'You came back alone,' she says. 'No word of my grandson. The pilgrims say there is an "
             "open place for anyone who hears the voice, and that the voice has gone quiet since you went in.' She turns the "
             "page. 'I have decided not to ask what you said to him at the door. I have decided that many times.' She "
             "slides a cup across the counter. 'Water is free. It will still be free when you want it.'",
             [leave("Take the cup.", tag="go")]),
        Node("cq_cp_after_neutral",
             "'They have stopped walking south,' Oda says. 'The red robes are gone from the gap. I will believe it when the "
             "water runs clear.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_gs_exposed",
             "'He drinks from the cistern like everyone else, now, and complains that it has no flavour. I have never heard "
             "a sweeter complaint.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_gs_turned",
             "'He is among the grey ones by the cistern. He fetches water and he looks at the road you went by. Give him "
             "time.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_gs_kin",
             "'No word. I pour his portion at dusk, the way my mother did for my father. The water goes into the sand and "
             "the sand does not complain.'",
             [go("Something else.", ROOT, tag="else")]),
    ],
    node_choices=[
        root_choice("root", "cq_gs_exposed", "Any word of your grandson?", "cq_gs_exposed", when=has_flag("flag.fork.flock_exposed")),
        root_choice("root", "cq_gs_turned", "Any word of your grandson?", "cq_gs_turned", when=has_flag("flag.fork.flock_turned")),
        root_choice("root", "cq_gs_kin", "Any word of your grandson?", "cq_gs_kin", when=has_flag("flag.fork.flock_kin")),
    ])

VANE = Extension(
    file="SunspireCaravanMaster.tres", dialogue_id="dialogue.sunspire_caravan_master",
    variants=[shield("flag.main.dry_wells_done"), at("flag.beat.gap_seen", "cq_dw_vane")],
    nodes=[
        Node("cq_dw_vane",
             "Idrys Vane stands beside three turned-back carts with the face of a man who has been doing sums in his head for "
             "a week. 'Gap's blocked. Not by soldiers, by pilgrims. Two hundred in red, filing south every dawn in a line "
             "you cannot push through without hurting someone, each carrying a stoppered jar of water they took from my own "
             "wells. They do not speak to me. They smile at me like I am the one who is lost.' He kicks a wheel. 'Twenty "
             "years I have driven this road and I never needed a sword. I have hired four this week. Ask Tamsin Reed what "
             "the jars are for. She is the only one of them who ever came back.'",
             [go("Something else.", ROOT, tag="else"), leave("I will ask her.", tag="go")]),
    ])

TAMSIN = Extension(
    file="SunspirePilgrim.tres", dialogue_id="dialogue.sunspire_pilgrim",
    variants=[
        shield("flag.main.prophets_flock_done"),
        at("flag.fork.flock_turned", "cq_pf_turned"),
        shield("flag.main.dry_wells_done"),
        at("flag.beat.gap_seen", "cq_dw_tamsin"),
    ],
    nodes=[
        Node("cq_dw_tamsin",
             "Tamsin Reed is crouched behind the cistern's far wall with her knees to her chest and a red robe balled in her "
             "lap that she has been trying to give away. 'The jars go south. Every pilgrim fills one at the wells before the "
             "walk, to be blessed at the altar, and they come back at midnight: not blessed, burned. Ash. They tip the ash "
             "into the cistern upstream from the oasis wall and call it communion. Whoever drinks it hears the voice "
             "better.' She scrubs a hand across her face. 'I drank it. Twice. It is the most beautiful thing I have ever "
             "heard and I have never been so frightened. He needs the wells because the wells are the only water in the "
             "Dominion. Everyone has to drink somewhere.' She looks up. 'The deacon at the chapel door keeps a ledger. "
             "Everyone's name is in it. If you want to stop it, that is where it stops.'",
             [go("Something else.", ROOT, tag="else"), leave("I will see the deacon.", tag="go")]),
        Node("cq_pf_turned",
             "Tamsin Reed has found a plain grey cloak from somewhere and wears it like armour. Behind her two dozen former "
             "pilgrims stand in a line with empty jars, looking at you the way people look at a lighthouse. 'They want to "
             "go home,' she says. 'They do not know where that is. I am taking them to the wells to teach them to fetch "
             "water. Real water.' Her voice shakes. 'You said no to the voice in front of them. Do you know what that did? "
             "It did not free them. It gave them somebody else to listen to.' She breathes. 'I will spend the rest of my "
             "life teaching them to listen to themselves. Please do not make it harder.'",
             [go("Something else.", ROOT, tag="else"), leave("I will not.", tag="go")]),
    ])

EXTENSIONS = [CLAN_CHIEF, CLAN_EXILE, CLAN_QUARTERMASTER, MAEVE, ADA, HASK, ODA, VANE, TAMSIN]

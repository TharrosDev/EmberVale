"""W-Spec-1 additions to HAND-AUTHORED dialogues (Act I and Act II NPCs). Applied by tools/campaign/extend_hand.py;
the locale rows are handed to the generator by specs/hand.py. Not discovered by the registry (leading underscore).

Start variants are evaluated top-down and the first match wins. Each beat's variant is therefore preceded by a
'shield': a variant that matches once the beat is finished and points back at `root`, so a finished beat can never
show again. A variant never targets a node with a flag that its own objective sets on the same frame.
"""

from tools.campaign.extend_hand import Extension
from tools.campaign.model import C, E, Node, StartVariant, go, has_flag, leave, say, missing_flag
from tools.campaign.specs._s1 import give, has_item, rep, take

ROOT = "root"


def shield(flag):
    return StartVariant((C.HAS_FLAG, flag), ROOT)


def at(flag, node):
    return StartVariant((C.HAS_FLAG, flag), node)


def active(qid, node):
    return StartVariant((C.QUEST_ACTIVE, qid), node)


def root_choice(node, tag, text, goto, when=None, idx=-1, **kw):
    return (node, idx, go(text, goto, tag=tag, when=when, **kw) if when else go(text, goto, tag=tag, **kw))


# ---------------------------------------------------------------------------------------------------
# Act I
# ---------------------------------------------------------------------------------------------------

ELDER = Extension(
    file="Elder.tres", dialogue_id="dialogue.elder",
    variants=[
        shield("flag.main.iron_king_done"),
        at("flag.rival.met1", "cq_ik_after"),
        shield("flag.main.smoke_over_the_square_done"),
        at("flag.beat.square_held", "cq_sq_after"),
        active("quest.main.smoke_over_the_square", "cq_sq_hook"),
    ],
    nodes=[
        Node("cq_sq_hook",
             "The Elder is already looking north when you reach him. A column of smoke stands over the Kingsway, thick and "
             "straight. 'Goblins raid by night, in threes, and run when they bleed. This is noon, and they are marching in "
             "step. That is not hunger. That is orders.' His eyes find the ember in yours and do not remark on it. 'Do not "
             "tell me what you are. Ring the bell.' He points at a post beside the Waystone where an old bronze bell hangs. "
             "'The town will bar its doors, and the square becomes the only thing between that smoke and the market. Hold it "
             "until they break.'",
             [go("Who is giving them orders?", "cq_sq_who", tag="who"),
              go("Something else.", ROOT, tag="else"),
              leave("I will ring it.", tag="go")]),
        Node("cq_sq_who",
             "'I have a notion, and I would rather be wrong.' He does not look away from the smoke. 'Ring the bell. Ask me "
             "again when the square is still standing.'",
             [go("Something else.", ROOT, tag="else"), leave("Understood.", tag="go")]),
        Node("cq_sq_after",
             "The Elder sits on the well's edge with a disc of black iron in his hand. 'It was around a goblin's neck. "
             "Goblins do not stamp iron. This is Citadel scrip, black iron, the kind the Iron King's marshals pay in, and it "
             "was paying for that warband.' He closes his fist on it. 'Someone on the spur above this square is arming the "
             "roads against us. I cannot prove it and I cannot stop it. Kael Aldemar can tell you what the last warband "
             "cost him. He is the one in the square with a sword and nothing to say. Ask him about the Ashfall pass, and "
             "then go and look at it. There may be more black iron out there than the Citadel's.'",
             [go("Something else.", ROOT, tag="else"), leave("I will find Kael.", tag="go")]),
        Node("cq_ik_after",
             "The Elder is on the well's edge as if he never left it, but the square behind him is full of people carrying "
             "things home. 'It is done, then.' He listens while you tell it, and when you reach the rider in the gallery he "
             "stops you with a raised hand. 'My grandmother's grandmother sang a counting rhyme at the washing, and nobody "
             "remembers who taught her. Six went up the Stair. Five fell down it. One stayed to kneel. I took it for a "
             "children's song.' He turns the token he has been holding over once more. 'Six climbed, so six were given the "
             "fire. The Iron King was the first of the five who fell, and you carry a piece of him now. The Storm Tyrant "
             "sits above the Frostfang clans. The Beast Lord holds a plateau beyond the Breach. The Crimson Prophet "
             "preaches in the Sunspire, where the old library stands. The song never agrees on the fifth.' He stands, "
             "slowly. 'At the top of the Stair, the rhyme says, there is a seat, and whoever takes it ends the dying or ends "
             "everything else. The ones who kept the records will know which. Do not go to any of the three alone, ember "
             "or no ember.'",
             [leave("I will go.", tag="go")]),
        Node("cq_dray_spared",
             "'Dray? He walked through the south gate yesterday with no rank cords and a limp, and asked the first warden "
             "he met whether the Watch took a man who had signed the wrong papers for eleven years. The warden said it took "
             "anyone who stood watches. He is on the wall tonight.' The Elder allows himself something near a smile. 'You "
             "freed a man the Citadel would have broken. The garrison remembers who unlocked that cell. It will matter.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_dray_pressed",
             "'Dray? Nobody has seen him. The garrison says the Marshal's strongbox came up empty and that he sits with his "
             "face to the wall.' The Elder's hand finds the rim of the well. 'I will not ask what you did to him. I will ask "
             "whether you can still be told no, afterwards. That is what the ember does, in the end. It makes every answer "
             "yes.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_arcs_done",
             "'Three of the five, then, and you walk like someone carrying more than he started with.' The Elder does not "
             "look north this time. He looks at you. 'The rhyme says the last verses are the worst and it has not been wrong "
             "yet. The Archivist in the Sunspire library keeps the records of the Stair. If anyone alive knows what the "
             "throne is for, she does. Go and ask her, and ask her plainly.'",
             [go("Something else.", ROOT, tag="else")]),
    ],
    node_choices=[
        root_choice("root", "cq_dray_spared", "About the Marshal.", "cq_dray_spared", when=has_flag("flag.fork.dray_spared")),
        root_choice("root", "cq_dray_pressed", "About the Marshal.", "cq_dray_pressed", when=has_flag("flag.fork.dray_pressed")),
        root_choice("root", "cq_arcs_done", "The three realms are quiet.", "cq_arcs_done", when=has_flag("flag.beat.arcs_complete")),
    ])

KAEL = Extension(
    file="Kael.tres", dialogue_id="dialogue.kael",
    variants=[shield("flag.main.the_pass_kept_done"), at("flag.beat.black_token_taken", "cq_pass_report")],
    nodes=[
        Node("cq_pass_report",
             "Kael does not look up from the whetstone until you set the token on the stone beside it. The scraping stops. "
             "'Where did you get this?' You tell him. He is quiet for a time, long enough that the blade across his knees "
             "goes dull in the light. 'Toren's cairn. Somebody tends it. I told myself it was the Citadel, shame money or a "
             "clerk's conscience, and never asked which. This is not Citadel make. Theirs is stamped square, like the one "
             "the Elder showed you. This is round and old, and the ring has a gap in it.' He turns the token until the gap "
             "faces him. 'Twelve years ago a rider in black iron stood at the end of that pass while twenty goblins went "
             "through us, and when it was over only the goblins were dead. I never saw his face. I never saw him again. I "
             "told myself it was Toren's ghost.'",
             [go("What does he want?", "cq_pass_orders", tag="want")]),
        Node("cq_pass_orders",
             "'Want? The Citadel sells iron to goblins, and somebody I cannot name sits at the other end of the same road "
             "and draws a ring on it. I do not know whether he is for us or against us, and I do not like how little that "
             "seems to matter to him.' He closes your fingers over the token. 'Keep it. It will not leave me alone, and you "
             "seem to be the one it was left for. The guild board has a bounty up for goblins. Take it, and look at what "
             "they carry. If there is more black iron out there, I want to hear it from you before I hear it from the "
             "Elder.'",
             [go("Something else.", ROOT, tag="else"), leave("I will.", tag="go")]),
    ])

SMITH = Extension(
    file="Smith.tres", dialogue_id="dialogue.smith",
    variants=[shield("flag.main.forge_done"), active("quest.warband.forge", "cq_forge_check")],
    nodes=[
        Node("cq_forge_check",
             "Bryn's anvil is cold and his forearms are black to the elbow where he has been stripping an old furnace for "
             "scrap. 'The Citadel's factors bought every ingot in the Crown last spring and never said what for. Now I know. "
             "Spearheads.' He wipes his hands on his apron and does not get any cleaner. 'Three lumps of ore from the hills "
             "and I will have the forge lit and a blade on this bench that never touched their scrap. Have you got it?'",
             [go("Here is the ore.", "cq_forge_thanks", tag="ore", when=has_item("item.material.iron_ore", 3),
                 do=take("item.material.iron_ore", 3)),
              go("Something else.", ROOT, tag="else"),
              leave("Not yet.", tag="later")]),
        Node("cq_forge_thanks",
             "He weighs the lumps in his palm one at a time, the way a man counts his children after a storm. 'Good iron. "
             "Clean.' The bellows wake with a roar and he does not look up for a long moment. 'There. A forge that answers "
             "to the town and not to the spur. If you are going where I think you are going, you will want steel that is "
             "ours.'",
             [leave("I will bring it back in one piece.", tag="go")]),
    ])

APOTHECARY = Extension(
    file="Apothecary.tres", dialogue_id="dialogue.apothecary",
    variants=[shield("flag.main.remedies_done"), active("quest.warband.remedies", "cq_rem_check")],
    nodes=[
        Node("cq_rem_check",
             "Mirela does not look up from her mortar, and the mortar is empty. 'Goblin wounds on every cot and Citadel "
             "stamps on the bandages. Do you know their factors bought my linen? I have stopped asking what for. Four "
             "sprigs of healing herb, you said you would bring. I will take them from your hand, not from your promise.'",
             [go("Here are the herbs.", "cq_rem_thanks", tag="herbs", when=has_item("item.material.healing_herb", 4),
                 do=take("item.material.healing_herb", 4)),
              go("Something else.", ROOT, tag="else"),
              leave("Not yet.", tag="later")]),
        Node("cq_rem_thanks",
             "She crushes one sprig between finger and thumb and breathes it in, and for the first time that day her "
             "shoulders drop. 'There. The cots will mend. Go and do whatever it is you are going to do to that spur.' Under "
             "her breath: 'Come back alive. It is the one prescription I never get to fill.'",
             [leave("I will try.", tag="go")]),
    ])

BROKER = Extension(
    file="SyndicateBroker.tres", dialogue_id="dialogue.syndicate_broker",
    variants=[shield("flag.main.crossway_whispers_done"), shield("flag.beat.armed_broker"), at("flag.rival.sigil", "cq_broker")],
    nodes=[
        Node("cq_broker",
             "Ilder Vance reads the stamp you copied onto the back of your hand without being asked to look at it. 'A ring "
             "with a gap. I could tell you what the Ledger House thinks that is and what it would cost you, but you are not "
             "buying a story. You are buying the quartermaster's consignment book, and who put his name to each line.' He "
             "names a number that is not low. 'Sixty, in coin, and the book says which of the Citadel's men signed for "
             "the arms. I do not sell opinions.'",
             [go("Pay sixty.", "cq_broker_paid", tag="pay", when=has_item("item.currency.gold", 60),
                 do=take("item.currency.gold", 60), do2=(E.SET_FLAG, "flag.beat.armed_broker")),
              go("Something else.", ROOT, tag="else"),
              leave("Not at that price.", tag="no")]),
        Node("cq_broker_paid",
             "He counts it twice, folds the coins away, and slides a page across the doorframe without letting go of it. "
             "'Orsolo Dray, Marshal of the Citadel garrison, on every line for eleven years. Make of that what you like. "
             "The Ledger House sold you a fact. What it means is yours.' He allows a very thin smile. 'The Syndicate "
             "remembers a customer who pays on delivery.'",
             [leave("Good day, broker.", tag="go")], on_enter=rep("faction.iron_syndicate", 8)),
    ])

FENN = Extension(
    file="DawnwardenCaptain.tres", dialogue_id="dialogue.dawnwarden_captain",
    variants=[shield("flag.main.crossway_whispers_done"), at("flag.beat.crate_opened", "cq_sigil"),
              active("quest.main.crossway_whispers", "cq_brief")],
    nodes=[
        Node("cq_brief",
             "Fenn finishes the line he is on before he looks up. 'A Citadel wax on a cart means do not open. I was told so "
             "in a letter signed by someone I have never met. Your banner carries the same quartermaster's stamp as those "
             "carts, so I am being ordered not to look at the very thing I was posted here to look at.' He closes the "
             "ledger over a finger. 'There is a crate in the Impound Counter that came up from Hollowreach last week under "
             "Citadel wax. My clerks log it and I cannot cut it. A traveller with a chisel answers to nobody's letters but "
             "her own.'",
             [go("Something else.", ROOT, tag="else"), leave("I will look.", tag="go")]),
        Node("cq_sigil",
             "You set a spearhead on his desk and the stamps beside it. Fenn touches neither. 'Garrison stores. The square "
             "is the Citadel quartermaster's. And that.' He taps the desk beside the ring with a gap. 'That is in the "
             "Citadel's own ledger too, in a hand older than any clerk they have, and every time I have asked who signs for "
             "it I have been offered a year's pay and a quieter road.' He takes a long breath. 'The square is easy. Orsolo "
             "Dray, Marshal of the garrison, has signed for every arms consignment through this gate. Either he is a "
             "traitor or a hostage, and I would give the year's pay to know which.'",
             [go("Then I will find out which.", "cq_honest", tag="honest", when=missing_flag("flag.beat.armed_by_known"),
                 do=(E.SET_FLAG, "flag.beat.armed_honest"), do2=rep("faction.dawnwardens", 5)),
              go("Something else.", ROOT, tag="else"),
              leave("Not yet.", tag="later")]),
        Node("cq_honest",
             "'Good. Not the answer I wanted, but the question was never going to be comfortable.' He opens the ledger "
             "again, which is how Fenn dismisses people. 'Go home first. The Citadel's factors bought every ingot in the "
             "Crown, and Bryn's forge has been cold for it. If you are going up that spur you will want steel that never "
             "went through their hands.'",
             [leave("I will see Bryn.", tag="go")]),
        Node("cq_heist_after",
             "Fenn does not look up. 'A page is missing from the carter ledger. It was there when the clerk went for his "
             "tea, so I have decided it was removed by a thief and not an investigator. Do not do that again, and do not "
             "tell me where it went.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_honest_after",
             "'The Marshal's name on the arms.' Fenn rubs his eyes. 'I have written it in the Watch's own book now, where "
             "the Citadel's clerks cannot lose it. Thank you for not making me read it alone.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_dray_spared",
             "'Dray walked in at the gate at dawn, no cords, no sword, and asked to be put on the book.' Fenn almost "
             "smiles. 'I put him on as a captain, which is above my authority and below his. He has not slept. He is "
             "sorting the warden roster by who can be trusted with a bow and he is right about two of them already.'",
             [go("Something else.", ROOT, tag="else")]),
        Node("cq_dray_pressed",
             "'Word came down the Kingsway that the Marshal's strongbox was emptied in his own cell.' Fenn does not raise his "
             "voice. 'The Watch does not approve of picking a man's pockets while he is chained. I am not saying you "
             "were wrong about what was in them. I am saying it will be remembered, here, and not kindly.'",
             [go("Something else.", ROOT, tag="else")]),
    ],
    node_choices=[
        root_choice("root", "cq_heist_after", "About the ledger.", "cq_heist_after", when=has_flag("flag.beat.armed_heist")),
        root_choice("root", "cq_honest_after", "About the Marshal's name.", "cq_honest_after", when=has_flag("flag.beat.armed_honest")),
        root_choice("root", "cq_dray_spared", "About Marshal Dray.", "cq_dray_spared", when=has_flag("flag.fork.dray_spared")),
        root_choice("root", "cq_dray_pressed", "About Marshal Dray.", "cq_dray_pressed", when=has_flag("flag.fork.dray_pressed")),
    ])

EXTENSIONS = [ELDER, KAEL, SMITH, APOTHECARY, BROKER, FENN]

# -*- coding: utf-8 -*-
"""Group A + B prompt table. STEM is verbatim from README.md 'The prompt stem'."""
STEM = ("Full-body front view of {subject}, T-pose, arms straight out to the sides, plain flat grey "
        "background. {detail} Muted desaturated ash-grey and faded-earth-brown palette, cold iron "
        "buckles, one small ember-orange accent. Semi-realistic AAA fantasy game character, grounded "
        "weathered Skyrim-like realism, physically based materials, believable cloth folds, worn "
        "leather and edge-worn metal. Adult proportions, 7.5 heads tall. No weapon, no scenery.")

# (archetype, asset_stem, height_m, radius, subject, detail)
GROUP_A = [
 ("Bandit","enm_bandit",1.8,0.40,"a human highway bandit",
  "Face half-wrapped in a dirty rag, patched leather jerkin over mismatched mail scraps, fingerless gloves, scuffed boots."),
 ("BarrowWight","enm_barrow_wight",1.7,0.40,"an undead barrow wight",
  "Gaunt desiccated grave-warden, sunken hollow eye sockets, tattered burial shroud over corroded scale, dirt-crusted greaves."),
 ("BoneKnight","enm_bone_knight",1.8,0.45,"a skeletal bone knight",
  "Fleshless armoured revenant, pitted rust-eaten plate, ribcage bare through a broken cuirass, ragged surcoat, closed visored helm."),
 ("CinderThrall","enm_cinder_thrall",1.8,0.40,"a burnt cinder thrall",
  "Charred husk of a person, cracked blackened skin glowing faintly along the fissures, scorched rags fused to the body, slack shoulders."),
 ("ClanBeastTamer","enm_clan_beast_tamer",1.8,0.40,"a northern clan beast tamer woman",
  "Thick fur-lined hide coat, bone-and-antler harness, braided hair, leather muzzles and collars hung from a broad belt, fur boots."),
 ("ClanRaider","enm_clan_raider",1.9,0.45,"a tall northern clan raider",
  "Heavyset warrior in furs and boiled hide over ring mail, painted war stripes on the face, bone beads in a braided beard, iron-shod boots."),
 ("ClanShaman","enm_clan_shaman",1.8,0.40,"a northern clan shaman",
  "Hooded ritual mantle of layered pelts, antlered headdress, bone fetishes and hanging charms, tattooed hands, wrapped shins."),
 ("Cultist","enm_cultist",1.8,0.40,"a robed fallen cultist",
  "Deep cowled robe with a sackcloth mask, knotted rope belt, ash-smeared bare forearms, a ritual brand burned into the chest, bare feet."),
 ("HollowHusk","enm_hollow_husk",1.75,0.40,"a hollow husk",
  "Emaciated animate corpse of a commoner, papery grey skin drawn tight, clouded eyes, rotted work tunic and torn breeches, bare feet."),
 ("HollowNecromancer","enm_hollow_necromancer",1.8,0.40,"a hollow necromancer woman",
  "Long trailing funeral gown over a high-collared robe, bone-clasp shoulder mantle, veiled face, ring-heavy skeletal hands."),
 ("Soldier","enm_soldier",1.85,0.45,"a fallen human soldier",
  "Disciplined man-at-arms in a dented breastplate over mail, a tabard faded past its device, gorget, gauntlets, tall riding boots."),
 ("SyndicateEnforcer","enm_syndicate_enforcer",1.8,0.40,"an Iron Syndicate enforcer",
  "Close-cut dark leather coat over a padded gambeson, iron-plated bracers, high collar, hood down, chain and hook at the belt."),
]
GROUP_B = [
 ("ArcaneEcho","enm_arcane_echo",1.8,0.40,"an arcane echo, the spectral afterimage of a mage",
  "Featureless hooded silhouette of translucent layered energy, robe hem and fingers dissolving into motes, no face."),
 ("GraveShade","enm_grave_shade",1.7,0.35,"a grave shade",
  "Slender shrouded wraith, a faceless void inside a hanging burial wrap, ragged trailing hem, thin skeletal arms and hands."),
 ("RimeShard","enm_rime_shard",1.6,0.40,"a rime shard elemental",
  "Jagged humanoid of fractured pale ice, splintered crystal limbs, frost-rimed shoulders, a hollow cracked core, no face."),
 ("StoneSentinel","enm_stone_sentinel",2.4,0.60,"a towering stone sentinel golem",
  "Massive carved granite construct, blocky weathered limbs, moss in the seams, iron bands at the joints, a faceless helm-like head."),
 ("WardGolem","enm_ward_golem",2.1,0.50,"a ward golem construct",
  "Broad animated guardian of carved basalt and riveted iron plate, sigils cut into the chest, heavy fists, no neck, a blank slab face."),
 ("AshenAcolyte","enm_ashen_acolyte",1.75,0.36,"an ashen acolyte",
  "Slight robed initiate coated in fine grey ash, shallow hood, ash-caked linen wrappings on the arms, sooted bare feet, head bowed."),
]
def prompt(row): return STEM.format(subject=row[4], detail=row[5])
if __name__ == "__main__":
    for g, rows in (("A", GROUP_A), ("B", GROUP_B)):
        for r in rows:
            p = prompt(r); flag = "  <<< OVER 600" if len(p) > 600 else ""
            print("%s %-18s %4d chars  h=%.2f%s" % (g, r[0], len(p), r[2], flag))

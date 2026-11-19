The EQL.Console app is a console app (obviously, right?).  This uses the exposed api from eqlwiki.com to get their list of items (https://eqlwiki.com/api.php).
I'm passing the following parameters to the api call:

action: query
generator: categorymembers
gcmtitle: Category:Items
gcmnamespace: 0
gcmlimit: 50
prop: revisions
rvprop: content
rvslots: main
format: json
formatversion: 2
maxlag: 5

This generates the json similar to:

{
    "batchcomplete": true,
    "continue": {
        "gcmcontinue": "page|324c422046455449442042415353|53412",
        "continue": "gcmcontinue||"
    },
    "query": {
        "pages": [
            {
                "pageid": 43645,
                "ns": 0,
                "title": "100' of Waterproofed Rope",
                "revisions": [
                    {
                        "slots": {
                            "main": {
                                "contentmodel": "wikitext",
                                "contentformat": "text/x-wiki",
                                "content": "{{Velious Era}}\n<onlyinclude>{{Itempage\n|notes       = \n|itemname    = 100' of Waterproofed Rope\n|lucy_img_ID = 1140\n|statsblock  = \nLORE ITEM  NO DROP<br>\nWT: 6.5  Size: MEDIUM<br>\nClass: ALL<br>\nRace: ALL<br>\n|dropsfrom = \n\n[[Cobalt Scar]]\n\n* [[a haunted seachest]]\n\n|relatedquests = \n\n* [[The Pirate Ship]]\n\n}}</onlyinclude>\n\n\n[[Category:Cobalt Scar]]\n\n[[Category:Quest Items]]"
                            }
                        }
                    }
                ]
            },...
        ]
    }
}

With the ASP.NET Core MVC web site, I have 2 steps basically.  1) Load items.csv which comes from running the console app.  2) Run /outputfile inventory (preferably with your dragonshoard
open at the bank so all inventory should be accounted for.  This will create a file similar to: <name>_<server>-inventory.txt.  I'm matching items based on the description in your
inventory to the overall items list in items.csv.  Once this is loaded, your character shows under the Inventories and you can click 'View' to see your inventory, 'Optimize' to see your 
best inventory fit that you have and against all possible items and 'Remove' to remove your character.

The Optimize button also does a few other things. You pick your 1-3 classes to draw on for your stats, you can change weights per stat if you want.  Maybe your caster is more for
intelligence than wisdom, flip the 1.5 and .5.  Once your satisfied, click 'Recalculate' to get the optimal loadout with your existing inventory.  You also get the optimal loadout with
the potential inventory.  You can then make your shopping list and go looking for the pieces you want.

# Product Images

This folder holds product images for the Contoso Outdoors catalog. These images are uploaded to Azure Blob Storage during the seeding process and served to agents via the Product Images MCP Server.

## Image files

Each product in `contoso-crm/products.csv` has an `image_filename` field that references an image file in this directory. The seed tool uploads all images found here to the `product-images` blob container.

## Placeholder images

For the workshop, placeholder images can be generated or sourced from royalty-free stock photo sites. The filenames must match the `image_filename` values in `products.csv`:

- `trailblazer-hiking-boots.png`
- `summit-approach-shoes.png`
- `alpine-summit-jacket.png`
- `waterproof-rain-shell.png`
- `basecamp-4p-tent.png`
- `ridgeline-65l-backpack.png`
- `ultralight-2p-tent.png`
- `summit-daypack-30l.png`
- `camp-lantern-led.png`
- `trail-kitchen-stove.png`
- `hydration-bladder-3l.png`
- `merino-base-layer-top.png`
- `merino-base-layer-bottom.png`
- `trekking-poles.png`
- `quick-dry-hiking-shorts.png`

> **Note:** The Product Images MCP Server and image seeding code are not yet implemented. This folder is prepared for future labs.

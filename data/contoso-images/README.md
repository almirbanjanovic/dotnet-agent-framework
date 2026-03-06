# Product Images

This folder holds product images for the Contoso Outdoors catalog. These images are uploaded to Azure Blob Storage during the seeding process and served to agents via the Product Images MCP Server.

## Image files

Each product in `contoso-crm/products.csv` has an `image_filename` field that references an image file in this directory. The seed tool uploads all images found here to the `product-images` blob container.

## Placeholder images

For the workshop, placeholder images can be generated or sourced from royalty-free stock photo sites. The filenames must match the `image_filename` values in `products.csv`:

- `trailblazer-hiking-boots.jpg`
- `summit-approach-shoes.jpg`
- `alpine-summit-jacket.jpg`
- `waterproof-rain-shell.jpg`
- `basecamp-4p-tent.jpg`
- `ridgeline-65l-backpack.jpg`
- `ultralight-2p-tent.jpg`
- `summit-daypack-30l.jpg`
- `camp-lantern-led.jpg`
- `trail-kitchen-stove.jpg`
- `hydration-bladder-3l.jpg`
- `merino-base-layer-top.jpg`
- `merino-base-layer-bottom.jpg`
- `trekking-poles.jpg`
- `quick-dry-hiking-shorts.jpg`

> **Note:** The Product Images MCP Server and image seeding code are not yet implemented. This folder is prepared for future labs.

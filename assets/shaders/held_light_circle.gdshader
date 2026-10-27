shader_type canvas_item;
render_mode unshaded;

uniform vec4 dim_color : source_color = vec4(0.005, 0.005, 0.008, 0.88);
uniform bool beam_enabled = false;
uniform vec2 beam_center_px = vec2(576.0, 324.0);
uniform vec2 viewport_size_px = vec2(1152.0, 648.0);
uniform float beam_radius_px = 152.0;
uniform float beam_softness_px = 2.0;
uniform vec4 beam_light_color : source_color = vec4(1.0, 0.92, 0.78, 0.12);

void fragment() {
    vec3 overlay_rgb = dim_color.rgb;
    float overlay_alpha = dim_color.a;

    if (beam_enabled) {
        // Match the room flashlight's horizontal, pixel-stepped circle.
        // This rect is full-viewport, so local UV maps to viewport pixels.
        vec2 pixel_position = UV * viewport_size_px;
        float strip_y = floor((pixel_position.y - beam_center_px.y) / 9.0) * 9.0
            + beam_center_px.y + 4.5;
        float distance_to_beam = length(vec2(
            pixel_position.x - beam_center_px.x,
            strip_y - beam_center_px.y));

        float inner_ring = smoothstep(
            beam_radius_px - beam_softness_px,
            beam_radius_px + beam_softness_px,
            distance_to_beam);
        float middle_ring = smoothstep(
            beam_radius_px + 54.0 - beam_softness_px,
            beam_radius_px + 54.0 + beam_softness_px,
            distance_to_beam);
        float outer_ring = smoothstep(
            beam_radius_px + 118.0 - beam_softness_px,
            beam_radius_px + 118.0 + beam_softness_px,
            distance_to_beam);

        // These three levels mirror the room's real nested darkness passes;
        // the soft center is the lit field that should fall over the item too.
        overlay_rgb = mix(beam_light_color.rgb, dim_color.rgb, inner_ring);
        overlay_alpha = mix(beam_light_color.a, 0.72, inner_ring);
        overlay_alpha = mix(overlay_alpha, 0.894, middle_ring);
        overlay_alpha = mix(overlay_alpha, 0.964, outer_ring);
    }

    vec4 incoming_modulate = COLOR;
    COLOR = vec4(overlay_rgb * incoming_modulate.rgb,
        overlay_alpha * incoming_modulate.a);
}

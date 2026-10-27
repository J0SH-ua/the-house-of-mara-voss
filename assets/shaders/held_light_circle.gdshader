shader_type canvas_item;
render_mode unshaded;

uniform vec4 dim_color : source_color = vec4(0.005, 0.005, 0.008, 0.88);
uniform bool beam_enabled = false;
uniform vec2 beam_center_px = vec2(576.0, 324.0);
uniform vec2 viewport_size_px = vec2(1152.0, 648.0);
uniform float beam_radius_px = 152.0;
uniform float beam_softness_px = 18.0;

void fragment() {
    float darkness = 1.0;
    if (beam_enabled) {
        vec2 pixel_position = SCREEN_UV * viewport_size_px;
        float distance_to_beam = distance(pixel_position, beam_center_px);
        darkness = smoothstep(
            beam_radius_px - beam_softness_px,
            beam_radius_px + beam_softness_px,
            distance_to_beam);
    }

    vec4 incoming_modulate = COLOR;
    COLOR = vec4(dim_color.rgb * incoming_modulate.rgb,
        dim_color.a * darkness * incoming_modulate.a);
}

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE users (
  user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  display_name TEXT NOT NULL CHECK (char_length(display_name) >= 1),
  experience_points INT NOT NULL DEFAULT 0 CHECK (experience_points >= 0),
  level INT NOT NULL DEFAULT 1 CHECK (level >= 1),
  coins INT NOT NULL DEFAULT 0 CHECK (coins >= 0),
  unlocked_characters TEXT[] NOT NULL DEFAULT ARRAY['robot_kyle'],
  selected_character_key TEXT NOT NULL DEFAULT 'robot_kyle',
  robot_color TEXT NOT NULL DEFAULT 'default',
  last_position_x DOUBLE PRECISION,
  last_position_y DOUBLE PRECISION,
  last_position_z DOUBLE PRECISION,
  last_rotation_y DOUBLE PRECISION,
  last_position_updated_at TIMESTAMP,
  mal_user_id TEXT,
  mal_username TEXT,
  mal_access_token TEXT,
  mal_refresh_token TEXT,
  mal_access_token_expires_at TIMESTAMP,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_users_display_name ON users(display_name);
CREATE INDEX idx_users_level ON users(level);

CREATE TABLE anime (
  anime_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  provider TEXT NOT NULL CHECK (char_length(provider) >= 1),
  provider_id TEXT NOT NULL CHECK (char_length(provider_id) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  title_english TEXT,
  title_japanese TEXT,
  title_spanish TEXT,
  title_synonyms TEXT[] NOT NULL DEFAULT '{}',
  image_url TEXT,
  synopsis TEXT,
  genres TEXT[] NOT NULL DEFAULT '{}',
  episodes INT CHECK (episodes IS NULL OR episodes >= 0),
  year INT CHECK (year IS NULL OR year >= 1900),
  trailer_youtube_id TEXT,
  created_at TIMESTAMP NOT NULL DEFAULT NOW(),
  UNIQUE(provider, provider_id)
);

CREATE INDEX idx_anime_title ON anime(title);
CREATE INDEX idx_anime_title_english ON anime(title_english);
CREATE INDEX idx_anime_title_japanese ON anime(title_japanese);
CREATE INDEX idx_anime_title_spanish ON anime(title_spanish);
CREATE INDEX idx_anime_title_synonyms ON anime USING GIN(title_synonyms);
CREATE INDEX idx_anime_year ON anime(year);
CREATE INDEX idx_anime_provider_provider_id ON anime(provider, provider_id);

CREATE TABLE quests (
  quest_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT NOT NULL UNIQUE CHECK (char_length(code) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  description TEXT NOT NULL,
  requirements JSONB NOT NULL DEFAULT '{}'::jsonb,
  rewards JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_quests_code ON quests(code);

CREATE TABLE user_quests (
  user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  quest_id UUID NOT NULL REFERENCES quests(quest_id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('active', 'completed', 'failed')),
  progress JSONB NOT NULL DEFAULT '{}'::jsonb,
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, quest_id)
);

CREATE INDEX idx_user_quests_status ON user_quests(status);
CREATE INDEX idx_user_quests_updated_at ON user_quests(updated_at);

CREATE TABLE watch_entries (
  user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  anime_id UUID NOT NULL REFERENCES anime(anime_id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('watching', 'completed', 'planned', 'dropped', 'on_hold')),
  score INT CHECK (score IS NULL OR (score >= 0 AND score <= 10)),
  episodes_watched INT NOT NULL DEFAULT 0 CHECK (episodes_watched >= 0),
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, anime_id)
);

CREATE INDEX idx_watch_entries_status ON watch_entries(status);
CREATE INDEX idx_watch_entries_updated_at ON watch_entries(updated_at);

CREATE TABLE achievements (
  achievement_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT NOT NULL UNIQUE CHECK (char_length(code) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  description TEXT NOT NULL,
  category TEXT NOT NULL CHECK (char_length(category) >= 1),
  icon TEXT,
  requirements JSONB NOT NULL DEFAULT '{}'::jsonb,
  points INT NOT NULL DEFAULT 0 CHECK (points >= 0),
  is_hidden BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_achievements_code ON achievements(code);
CREATE INDEX idx_achievements_category ON achievements(category);

CREATE TABLE user_achievements (
  user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  achievement_id UUID NOT NULL REFERENCES achievements(achievement_id) ON DELETE CASCADE,
  status TEXT NOT NULL DEFAULT 'locked' CHECK (status IN ('locked', 'in_progress', 'unlocked')),
  progress JSONB NOT NULL DEFAULT '{}'::jsonb,
  unlocked_at TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, achievement_id)
);

CREATE INDEX idx_user_achievements_status ON user_achievements(status);
CREATE INDEX idx_user_achievements_unlocked_at ON user_achievements(unlocked_at);

INSERT INTO quests (code, title, description, requirements, rewards) VALUES
  ('watch_5_eps', 'Warm-up Marathon', 'Watch five anime episodes this week.', '{"episodes":5}'::jsonb, '{"xp":50,"coins":100,"character":"robot_blue"}'::jsonb),
  ('rate_3_titles', 'Critic Apprentice', 'Rate three different anime titles.', '{"ratings":3}'::jsonb, '{"xp":40,"item":"review_badge","character":"robot_green"}'::jsonb),
  ('complete_series', 'Finale Hunter', 'Complete one anime series.', '{"completed_series":1}'::jsonb, '{"xp":100,"coins":250,"character":"ghost_character"}'::jsonb),
  ('watch_12_eps', 'Season Sprint', 'Watch twelve anime episodes.', '{"episodes":12}'::jsonb, '{"xp":90,"coins":160}'::jsonb),
  ('watch_24_eps', 'Binge Legend', 'Watch twenty-four anime episodes.', '{"episodes":24}'::jsonb, '{"xp":160,"coins":320}'::jsonb),
  ('rate_5_titles', 'Sharp-Eyed Critic', 'Rate five different anime titles.', '{"ratings":5}'::jsonb, '{"xp":90,"coins":140,"item":"critic_pin"}'::jsonb),
  ('complete_3_series', 'Completionist Path', 'Complete three anime series.', '{"completed_series":3}'::jsonb, '{"xp":220,"coins":500}'::jsonb),
  ('balanced_fan', 'Balanced Fan', 'Watch, rate, and finish anime to prove a rounded profile.', '{"episodes":10,"ratings":2,"completed_series":1}'::jsonb, '{"xp":150,"coins":250,"item":"balanced_badge"}'::jsonb)
ON CONFLICT (code) DO UPDATE SET
  title = EXCLUDED.title,
  description = EXCLUDED.description,
  requirements = EXCLUDED.requirements,
  rewards = EXCLUDED.rewards;

INSERT INTO achievements (code, title, description, category, icon, requirements, points, is_hidden) VALUES
  ('first_login', 'Welcome, Adventurer', 'Log in for the first time.', 'profile', 'spark', '{"logins":1}'::jsonb, 10, FALSE),
  ('quest_runner', 'Quest Runner', 'Complete your first quest.', 'quests', 'scroll', '{"completed_quests":1}'::jsonb, 25, FALSE),
  ('anime_marathon', 'Anime Marathon', 'Watch 50 episodes total.', 'watching', 'clapperboard', '{"episodes_watched":50}'::jsonb, 50, FALSE)
ON CONFLICT (code) DO NOTHING;

